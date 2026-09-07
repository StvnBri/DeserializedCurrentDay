Imports MongoDB.Bson
Imports MongoDB.Driver
Imports Newtonsoft
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq
Imports System.Data
Imports System.Text.RegularExpressions
Imports System.Dynamic
Imports System.IO
Imports MongoDB.Bson.IO
Imports MongoDB.Bson.Serialization
Imports System.Text
Imports System.Data.SqlClient

Public Class frmmain

    Public connectionStringHRDW = Configuration.ConfigurationSettings.AppSettings("connectionStringDW")
    Public objconnectionautohrdwLoop As New SqlConnection(connectionStringHRDW)
    Public SQLCommandLoop As SqlCommand
    Public SQLReaderLoop As SqlDataReader

    Public objconnectionautohrdw As New SqlConnection(connectionStringHRDW)
    Public SQLCommand As SqlCommand
    Public SQLReader As SqlDataReader

    Public objconnectionautohrdwError As New SqlConnection(connectionStringHRDW)
    Public SQLCommandError As SqlCommand

    Dim _client As IMongoClient
    Dim _db As IMongoDatabase
    Public DestinationTable As String
    Public MongoDBConnectionString As String
    Public SourceDocument As String
    Public TargetTable As String
    Public Lockid As Integer
    Public FilterField1 As String
    Public FilterField2 As String
    Public FilterField3 As String
    Dim customdate As Date
    Dim querystring As String

    Private Sub frmmain_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Get_MongDB_Credentials()
        Get_Source_Target()
        End
    End Sub

    Public Sub Get_MongDB_Credentials()
        objconnectionautohrdw.Open()
        SQLCommand = New SqlCommand("sproc_get_arcusair_uat_credentials", objconnectionautohrdw)
        SQLCommand.CommandType = CommandType.StoredProcedure
        SQLReader = SQLCommand.ExecuteReader(Data.CommandBehavior.CloseConnection)

        If SQLReader.Read Then
            MongoDBConnectionString = SQLReader("AAConnectionString")
        Else
            MsgBox("Credentials Not Found")
            End
        End If
        objconnectionautohrdw.Close()
    End Sub

    Public Sub Get_Source_Target()


        Try
            customdate = Now.AddDays(-1).ToShortDateString
            'customdate = "01/01/2000" ' For Testing only.
            objconnectionautohrdwLoop.Open()
            SQLCommandLoop = New SqlCommand("sproc_get_ArcusAir_Reference_Target_Transactional_CurrentDay", objconnectionautohrdwLoop)
            SQLCommandLoop.CommandType = CommandType.StoredProcedure
            SQLReaderLoop = SQLCommandLoop.ExecuteReader(Data.CommandBehavior.CloseConnection)

            Do While SQLReaderLoop.Read
                SourceDocument = SQLReaderLoop("Source_Document") ' where it came from/Source
                TargetTable = SQLReaderLoop("Target_Table") ' where to write it/ Destination
                FilterField1 = SQLReaderLoop("Filter1")

                Try
                    FilterField2 = SQLReaderLoop("Filter2") & ""
                Catch ex As Exception
                    FilterField2 = ""
                End Try

                Try
                    FilterField3 = SQLReaderLoop("Filter3") & ""
                Catch ex As Exception
                    FilterField3 = ""
                End Try


                Clear_Destination(TargetTable, FilterField1)
                Extract_Data_From_MongoDB(MongoDBConnectionString, SourceDocument, TargetTable, FilterField1)

                If FilterField2.Length > 0 Then
                    Extract_Data_From_MongoDB(MongoDBConnectionString, SourceDocument, TargetTable, FilterField2)
                End If

                If FilterField3.Length > 0 Then
                    Extract_Data_From_MongoDB(MongoDBConnectionString, SourceDocument, TargetTable, FilterField3)
                End If


                Unpacking_Process(TargetTable)
            Loop
            objconnectionautohrdwLoop.Close()
        Catch ex As Exception
            objconnectionautohrdwLoop.Close()
            ' StartLog("Get_Source_Target " & SourceDocument, ex.Message, 0)
        End Try
    End Sub

    Public Sub Clear_Destination(ReferenceTbl As String, RefField As String)
        Try
            'querystring = "Delete From " & ReferenceTbl & " where CONVERT(date, [" & RefField & "]) = '" & customdate.ToString("yyyy-MM-dd") & "'"
            querystring = "Delete From " & ReferenceTbl & "  where CONVERT(date, " & RefField & ") = '" & customdate & "'"
            objconnectionautohrdw.Open()
            SQLCommand = New Data.SqlClient.SqlCommand(querystring, objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.Text
            SQLCommand.ExecuteNonQuery()
            objconnectionautohrdw.Close()
        Catch ex As Exception
            objconnectionautohrdw.Close()
            StartLog("Clear_Destination", ex.Message, 0)
        End Try
    End Sub

    Public Sub Extract_Data_From_MongoDB(mongodbstr As String, SDocument As String, TTable As String, Filter As String)
        Dim lcnt As Integer = 0
        Dim vcnt As Integer
        Dim dtfull As New DataTable

        Dim mongo As MongoClient = New MongoClient(mongodbstr)
        Dim db = mongo.GetDatabase("arcusairdb")
        Dim collection = db.GetCollection(Of BsonDocument)(SDocument)

        Dim startDate As DateTime = New DateTime(customdate.Year, customdate.Month, customdate.Day)
        Dim f = Builders(Of BsonDocument).Filter.And(Builders(Of BsonDocument).Filter.Gte(Of Date)(Filter, startDate))
        Dim list = collection.Find(f).ToList

        StartLog(SDocument, TTable, list.Count)

        Do Until lcnt = list.Count
            dtfull.Rows.Add()
            vcnt = 0

            Do Until vcnt = list.Item(lcnt).Values.Count
                Try
                    Dim element = list.Item(lcnt).ElementAt(vcnt)
                    Dim colName As String = element.Name
                    Dim colValue = element.Value

                    If Not dtfull.Columns.Contains(colName) Then
                        dtfull.Columns.Add(colName, GetType(String))
                    End If

                    ' Store Raw BSON string in the column
                    dtfull.Rows(0)(colName) = colValue.ToString()
                    vcnt = vcnt + 1
                Catch ex As Exception
                    'StartLog("Extract_Data_From_MongoDB_Loop", SDocument & vbCrLf & ex.Message, 0)
                End Try
            Loop

            Delete_Table_By_Reference(TTable, dtfull.Rows.Item(0).Item("_id"))
            Process_Data_Transfer(TTable, dtfull)

            dtfull.Rows.Clear()
            dtfull.Columns.Clear()
            lcnt = lcnt + 1
        Loop
        EndLog(Lockid, lcnt)
    End Sub


    Public Function Get_Detail_Tables(MainTable As String) As DataTable
        Dim dtDetails As New DataTable()
        Try
            If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
            objconnectionautohrdw.Open()
            SQLCommand = New Data.SqlClient.SqlCommand("sproc_get_main_detail_table", objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.StoredProcedure
            SQLCommand.Parameters.AddWithValue("@Main_Table", MainTable)
            Dim SQLAdapter As New Data.SqlClient.SqlDataAdapter(SQLCommand)
            SQLAdapter.Fill(dtDetails)
            objconnectionautohrdw.Close()
            Return dtDetails
        Catch ex As Exception
            StartLog(MainTable, "Get_Detail_Tables Error: " & vbCrLf & ex.Message, 0)
            If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
            Return New DataTable()
        End Try
    End Function


    ' *** Unpacking sql to sql transaction

    Public Sub Unpacking_Process(MainTable As String)
        Try
            Dim dtMapping As New DataTable
            objconnectionautohrdw.Open()
            ' Find which columns in this MainTable need unpacking
            SQLCommand = New SqlCommand("sp_get_detail", objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.StoredProcedure
            'SQLCommand.Parameters.AddWithValue("@MainTable", MainTable)
            SQLCommand.Parameters.Add("@Main_Table", SqlDbType.NVarChar, 255).Value = MainTable
            'SQLCommand.Parameters.Add("@Detail_Table", SqlDbType.NVarChar, 255)
            SQLReader = SQLCommand.ExecuteReader()
            dtMapping.Load(SQLReader)
            'If dtMapping.Rows.Count = 0 Then
            'MsgBox("Mapping not found for table: " & MainTable)
            'Return
            'End If

            objconnectionautohrdw.Close()

            For Each mapRow As DataRow In dtMapping.Rows
                Dim targetDetailTable As String = mapRow("Detail_Table").ToString()
                Dim arrayFieldName As String = mapRow("FieldName").ToString()

                If String.IsNullOrEmpty(arrayFieldName) Then Continue For



                Dim dtMissingData As New DataTable
                objconnectionautohrdw.Open()

                ' Use the new Stored Procedure
                SQLCommand = New SqlCommand("sproc_get_missing_array_data", objconnectionautohrdw)
                SQLCommand.CommandType = CommandType.StoredProcedure

                ' Pass parameters
                SQLCommand.Parameters.AddWithValue("@Main_Table", MainTable)
                SQLCommand.Parameters.AddWithValue("@Detail_Table", targetDetailTable)
                SQLCommand.Parameters.AddWithValue("@FieldName", arrayFieldName)

                SQLReader = SQLCommand.ExecuteReader()
                dtMissingData.Load(SQLReader)
                objconnectionautohrdw.Close()

                ' Extract from SQL result set and load to Detail
                For Each dataRow As DataRow In dtMissingData.Rows
                    Dim parentId As String = dataRow("_id").ToString()
                    Dim rawBson As String = dataRow(arrayFieldName).ToString()

                    If Not String.IsNullOrEmpty(rawBson) AndAlso (rawBson.StartsWith("[") Or rawBson.StartsWith("{")) Then
                        Desrialized_Json(rawBson, parentId, targetDetailTable)
                    End If
                Next
            Next
        Catch ex As Exception
            If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
            'StartLog("Unpacking_Process", MainTable & vbCrLf & ex.Message, 0)
        End Try
    End Sub

    Public Sub Process_Data_Transfer(sourcetablename As String, sourcetable As DataTable)

        Dim columnstr As String

        Try
            objconnectionautohrdw.Open()
            Using SQLBulkCopy As SqlBulkCopy = New SqlBulkCopy(objconnectionautohrdw)
                For Each c As DataColumn In sourcetable.Columns
                    SQLBulkCopy.ColumnMappings.Add(c.ColumnName, c.ColumnName)
                    columnstr = columnstr & "," & c.ColumnName
                Next
                SQLBulkCopy.DestinationTableName = sourcetablename
                SQLBulkCopy.WriteToServer(sourcetable.CreateDataReader)
            End Using
            objconnectionautohrdw.Close()
        Catch ex As Exception
            objconnectionautohrdw.Close()
            '  MsgBox(ex.Message)
            StartLog(sourcetablename, "Process_Data_Transfer " & columnstr & vbCrLf & ex.Message, 0)
        End Try
    End Sub

    Public Sub Desrialized_Json(jsonfile As String, refid As String, TargetTable As String)
        Try
            ' Clean JSON string for Newtonsoft
            'Dim cleanedJson As String = jsonfile
            ' If Not cleanedJson.StartsWith("[") Then cleanedJson = "[" & cleanedJson & "]"

            'Dim ResultTable As DataTable = JsonConvert.DeserializeObject(Of DataTable)(cleanedJson)
            Dim ResultTable As DataTable = Newtonsoft.Json.JsonConvert.DeserializeObject(Of DataTable)(jsonfile)
            ' Dim ResultTable As DataTable = Newtonsoft.Json.JsonConvert.DeserializeObject(Of DataTable)(cleanedJson)

            'If Not ResultTable.Columns.Contains("reference_id") Then
            Dim newColumn As New DataColumn("reference_id", GetType(String))
            newColumn.DefaultValue = refid
            ResultTable.Columns.Add(newColumn)
            ' Ensure the default value applies to existing rows
            For Each row As DataRow In ResultTable.Rows
                row("reference_id") = refid
            Next
            ' End If

            Process_Data_Transfer(TargetTable, ResultTable)
        Catch ex As Exception
            StartLog(TargetTable, refid & vbCrLf & ex.Message & vbCrLf & jsonfile, 0)
        End Try
    End Sub



    Public Sub Delete_Table_By_Reference(TargetTable As String, TableReferenceID As String)
        Try
            querystring = "Delete From [" & TargetTable & "] where _id = '" & TableReferenceID & "'"
            objconnectionautohrdw.Open()
            SQLCommand = New SqlCommand(querystring, objconnectionautohrdw)
            SQLCommand.ExecuteNonQuery()
            objconnectionautohrdw.Close()
        Catch ex As Exception
            objconnectionautohrdw.Close()
            StartLog("Delete_Table_By_Reference", ex.Message, 0)
        End Try
    End Sub

    Public Function StartLog(Sdocument As String, TTable As String, SDocCount As Integer)
        Try
            objconnectionautohrdw.Open()
            SQLCommand = New SqlCommand("sproc_save_logs", objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.StoredProcedure
            SQLCommand.Parameters.AddWithValue("@Reference", Sdocument)
            SQLCommand.Parameters.AddWithValue("@Destination", TTable)
            SQLCommand.Parameters.AddWithValue("@ReferenceDocumentCount", SDocCount)
            SQLReader = SQLCommand.ExecuteReader(Data.CommandBehavior.CloseConnection)
            If SQLReader.Read Then Lockid = SQLReader("LockID")
            objconnectionautohrdw.Close()
        Catch ex As Exception
            objconnectionautohrdw.Close()
        End Try
    End Function

    Public Sub EndLog(lckid As Integer, DescCount As Integer)
        Try
            objconnectionautohrdw.Open()
            SQLCommand = New SqlCommand("sproc_save_endlogs", objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.StoredProcedure
            SQLCommand.Parameters.AddWithValue("@DestinationRowsCount", DescCount)
            SQLCommand.Parameters.AddWithValue("@LockID", lckid)
            SQLCommand.ExecuteNonQuery()
            SQLCommand.ExecuteNonQuery()
            objconnectionautohrdw.Close()
        Catch ex As Exception
            objconnectionautohrdw.Close()
        End Try
    End Sub

End Class