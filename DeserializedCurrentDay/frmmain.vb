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


Public Class frmmain

    Public connectionStringHRDW = Configuration.ConfigurationSettings.AppSettings("connectionStringDW")

    Public objconnectionautohrdwLoop As New Data.SqlClient.SqlConnection(connectionStringHRDW)
    Public SQLCommandLoop As Data.SqlClient.SqlCommand
    Public SQLReaderLoop As Data.SqlClient.SqlDataReader

    Public objconnectionautohrdw As New Data.SqlClient.SqlConnection(connectionStringHRDW)
    Public SQLCommand As Data.SqlClient.SqlCommand
    Public SQLReader As Data.SqlClient.SqlDataReader

    Public objconnectionautohrdwError As New Data.SqlClient.SqlConnection(connectionStringHRDW)
    Public SQLCommandError As Data.SqlClient.SqlCommand

    Dim _client As IMongoClient
    Dim _db As IMongoDatabase
    Dim dt As New DataTable
    Dim dtval As New DataTable
    Dim ds As New BindingSource
    Public DestinationTable As String
    Public dttablejson As New DataTable

    Public MongoDBConnectionString As String
    Public SourceDocument As String
    Public TargetTable As String
    Public querystring As String
    Public Lockid As Integer
    Public FilterField As String
    Public FilterField2 As String
    Public FilterField3 As String
    Dim customdate As Date

    Private Sub frmmain_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Get_MongDB_Credentials()
        Get_Source_Target()
        End
    End Sub

    Public Sub Get_MongDB_Credentials()
        objconnectionautohrdw.Open()
        SQLCommand = New Data.SqlClient.SqlCommand("sproc_get_arcusair_uat_credentials", objconnectionautohrdw)
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
            'customdate = "1/1/2000" ' use for testing only
            objconnectionautohrdwLoop.Open()
            SQLCommandLoop = New Data.SqlClient.SqlCommand("sproc_get_ArcusAir_Reference_Target_Transactional_CurrentDay", objconnectionautohrdwLoop)
            SQLCommandLoop.CommandType = CommandType.StoredProcedure
            SQLReaderLoop = SQLCommandLoop.ExecuteReader(Data.CommandBehavior.CloseConnection)

            Do While SQLReaderLoop.Read
                SourceDocument = SQLReaderLoop("Source_Document")
                TargetTable = SQLReaderLoop("Target_Table")
                FilterField = SQLReaderLoop("Filter1")

                Try
                    FilterField2 = SQLReaderLoop("Filter2")
                Catch ex As Exception
                    FilterField2 = ""
                End Try

                Try
                    FilterField3 = SQLReaderLoop("Filter3")
                Catch ex As Exception
                    FilterField3 = ""
                End Try

                Clear_Destination(TargetTable, FilterField)
                Extract_Data_From_MongoDB(MongoDBConnectionString, SourceDocument, TargetTable, FilterField)

                If FilterField2.Length > 0 Then
                    Extract_Data_From_MongoDB(MongoDBConnectionString, SourceDocument, TargetTable, FilterField2)
                End If

                If FilterField3.Length > 0 Then
                    Extract_Data_From_MongoDB(MongoDBConnectionString, SourceDocument, TargetTable, FilterField3)
                End If
            Loop

            objconnectionautohrdwLoop.Close()

        Catch ex As Exception
            objconnectionautohrdwLoop.Close()
            StartLog("Get_Source_Target " & SourceDocument, ex.Message, 0)
        End Try
    End Sub

    Public Sub Clear_Destination(ReferenceTbl As String, RefField As String)
        Try
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


    ' REVISED: Get_Detail_Tables - returns Field_Name from configuration

    Public Function Get_Detail_Tables(MainTable As String) As DataTable
        Dim dtDetails As New DataTable()
        Try
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


    ' REVISED processes arrays using Field_Name

    Public Sub Extract_Data_From_MongoDB(mongodbstr As String, SDocument As String, TTable As String, Filter As String)

        Dim lcnt As Integer
        Dim vcnt As Integer
        Dim dt As New DataTable
        Dim dr As DataRow

        Dim mongo As MongoClient = New MongoClient(mongodbstr)
        Dim db = mongo.GetDatabase("arcusairdb")
        Dim collection = db.GetCollection(Of BsonDocument)(SDocument)

        Dim startDate As DateTime = New DateTime(customdate.Year, customdate.Month, customdate.Day)
        Dim f = Builders(Of BsonDocument).Filter.Gte(Of Date)(Filter, startDate)
        Dim list = collection.Find(f).ToList

        StartLog(SDocument, TTable, list.Count)

        ' Get detail table mappings with Field_Name
        Dim dtDetailMap As DataTable = Get_Detail_Tables(TTable)

        Do Until lcnt = list.Count

            ' Get current document and extract _id FIRST
            Dim currentDoc As BsonDocument = list.Item(lcnt)
            Dim parentID As String = ""

            ' Extract the _id from the document immediately
            If currentDoc.Contains("_id") Then
                parentID = currentDoc("_id").ToString()
            End If

            ' Delete existing records BEFORE building DataTable
            If Not String.IsNullOrEmpty(parentID) Then
                Delete_Table_By_Reference(TTable, parentID)
                Delete_Detail_Table_By_ReferenceID(TTable, parentID)
            End If

            ' Now build the DataTable for parent record
            dt.Rows.Add()
            vcnt = 0

            ' Build parent table columns and extract data
            Do Until vcnt = currentDoc.Values.Count
                Try
                    vcnt = vcnt + 1
                    Dim elementName As String = currentDoc.ElementAt(vcnt - 1).Name.ToString
                    Dim elementValue As String = currentDoc.Values(vcnt - 1).ToString

                    ' Add column if it doesn't exist
                    If Not dt.Columns.Contains(elementName) Then
                        dt.Columns.Add(elementName, GetType(String))
                    End If

                    dt.Rows(0)(elementName) = elementValue

                Catch ex As Exception
                    StartLog("Extract_Data_From_MongoDB", SDocument & vbCrLf & ex.Message, 0)
                End Try
            Loop

            ' Insert parent record
            Process_Data_Transfer(TTable, dt)

            ' Process array fields using Field_Name from configuration
            If dtDetailMap.Rows.Count > 0 Then
                For Each detailRow As DataRow In dtDetailMap.Rows
                    Try
                        Dim detailTable As String = detailRow("Detail_Table").ToString()
                        Dim fieldName As String = detailRow("Field_Name").ToString()
                        Dim detailLevel As Integer = CInt(detailRow("Detail_Level"))

                        ' Only process Level 1 arrays (direct children)
                        If detailLevel = 1 AndAlso Not String.IsNullOrEmpty(fieldName) Then
                            Process_Array_Field(currentDoc, parentID, TTable, detailTable, fieldName)
                        End If
                    Catch ex As Exception
                        StartLog("Extract_Data_From_MongoDB", "Array Processing Error: " & detailRow("Detail_Table").ToString() & vbCrLf & ex.Message, 0)
                    End Try
                Next
            End If

            '  Clear DataTable for next iteration
            dt.Rows.Clear()
            dt.Columns.Clear()

            lcnt = lcnt + 1

        Loop

        EndLog(Lockid, list.Count)

    End Sub


    Public Sub Unpacking_process()

    End Sub

    ' Added Process_Array_Field - Unpacks array fields into child tables

    Public Sub Process_Array_Field(ParentDoc As BsonDocument, ParentID As String, MainTable As String, DetailTable As String, FieldName As String)

        Dim dtDetail As New DataTable()

        Try
            ' Check if parent document contains the array field
            If Not ParentDoc.Contains(FieldName) Then
                Exit Sub
            End If

            If Not ParentDoc.GetValue(FieldName).IsBsonArray Then
                Exit Sub
            End If

            Dim arrayData As BsonArray = ParentDoc.GetValue(FieldName).AsBsonArray

            ' Exit if array is empty
            If arrayData.Count = 0 Then
                Exit Sub
            End If

            ' Add reference_id column first (Foreign Key)
            dtDetail.Columns.Add("reference_id", GetType(String))

            ' Get column structure from first item in array
            If arrayData.Item(0).IsBsonDocument Then
                Dim firstDoc As BsonDocument = arrayData.Item(0).AsBsonDocument
                For Each element As BsonElement In firstDoc.Elements
                    If Not dtDetail.Columns.Contains(element.Name.ToString) Then
                        dtDetail.Columns.Add(element.Name.ToString, GetType(String))
                    End If
                Next
            End If

            ' Process each item in the array
            For Each arrayItem As BsonValue In arrayData
                If arrayItem.IsBsonDocument Then
                    Dim childDoc As BsonDocument = arrayItem.AsBsonDocument
                    Dim drDetail As DataRow = dtDetail.NewRow()

                    ' Set the foreign key linking to parent
                    drDetail("reference_id") = ParentID

                    ' Extract all fields from child document
                    For Each childElement As BsonElement In childDoc.Elements
                        Dim colName As String = childElement.Name.ToString

                        If dtDetail.Columns.Contains(colName) Then
                            Dim colValue As String

                            ' Handle nested arrays/documents (Level 2+)
                            If childElement.Value.IsBsonArray OrElse childElement.Value.IsBsonDocument Then
                                ' Store as JSON for potential further processing
                                colValue = childElement.Value.ToJson()
                            Else
                                ' Regular field
                                colValue = childElement.Value.ToString().Replace("[]", "").Replace("null", "")
                            End If

                            drDetail(colName) = colValue
                        End If
                    Next

                    dtDetail.Rows.Add(drDetail)

                    '  Handle nested arrays (Level 2) - Uncomment if needed
                    ' Dim dtGrandchildMap As DataTable = Get_Detail_Tables(DetailTable)
                    ' If dtGrandchildMap.Rows.Count > 0 Then
                    '     Dim childID As String = If(childDoc.Contains("_id"), childDoc("_id").ToString(), ParentID)
                    '     For Each grandchildRow As DataRow In dtGrandchildMap.Rows
                    '         Dim grandchildTable As String = grandchildRow("Detail_Table").ToString()
                    '         Dim grandchildFieldName As String = grandchildRow("Field_Name").ToString()
                    '         Process_Array_Field(childDoc, childID, DetailTable, grandchildTable, grandchildFieldName)
                    '     Next
                    ' End If
                End If
            Next

            ' Bulk insert the child records
            If dtDetail.Rows.Count > 0 Then
                Process_Data_Transfer(DetailTable, dtDetail)
            End If

        Catch ex As Exception
            StartLog(MainTable, "Process_Array_Field Error: " & DetailTable & " - " & FieldName & vbCrLf & ex.Message, 0)
        End Try

    End Sub




    Public Sub LoadBsonDetail(RefID As String, bsonfile As String, TargetTable As String)

    End Sub

    Public Sub Delete_Table_By_Reference(TargetTable As String, TableReferenceID As String)
        Try
            querystring = "Delete From " & TargetTable & "  where _id = '" & TableReferenceID & "'"
            objconnectionautohrdw.Open()
            SQLCommand = New Data.SqlClient.SqlCommand(querystring, objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.Text
            SQLCommand.ExecuteNonQuery()
            objconnectionautohrdw.Close()
        Catch ex As Exception
            objconnectionautohrdw.Close()
            StartLog("Delete_Table_By_Reference", ex.Message, 0)
        End Try
    End Sub

    Public Sub Delete_Detail_Table_By_ReferenceID(TargetTable As String, TableReferenceID As String)
        Dim dtdetailtable As New DataTable
        Dim detailcount As Integer = 0

        Try
            dtdetailtable.Clear()
            objconnectionautohrdw.Open()
            SQLCommand = New Data.SqlClient.SqlCommand("sproc_get_main_detail_table", objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.StoredProcedure
            SQLCommand.Parameters.Add("@Main_Table", SqlDbType.NVarChar, 50, "@Main_Table")
            SQLCommand.Parameters("@Main_Table").Value = TargetTable
            SQLReader = SQLCommand.ExecuteReader(Data.CommandBehavior.CloseConnection)
            dtdetailtable.Load(SQLReader)
            objconnectionautohrdw.Close()

            Do Until dtdetailtable.Rows.Count = detailcount
                querystring = "Delete From " & dtdetailtable.Rows(detailcount)("Detail_Table").ToString & "  where reference_id = '" & TableReferenceID & "'"
                objconnectionautohrdw.Open()
                SQLCommand = New Data.SqlClient.SqlCommand(querystring, objconnectionautohrdw)
                SQLCommand.CommandType = CommandType.Text
                SQLCommand.ExecuteNonQuery()
                objconnectionautohrdw.Close()
                detailcount = detailcount + 1
            Loop

        Catch ex As Exception
            objconnectionautohrdw.Close()
            StartLog("Delete_Detail_Table_By_ReferenceID", "Delete_Detail_Table_By_ReferenceID" & vbCrLf & ex.Message, 0)
        End Try
    End Sub

    Public Sub Process_Data_Transfer(sourcetablename As String, sourcetable As DataTable)
        Dim columnstr As String

        Try
            objconnectionautohrdw.Open()
            Using SQLBulkCopy As SqlClient.SqlBulkCopy = New SqlClient.SqlBulkCopy(objconnectionautohrdw)

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
            StartLog(sourcetablename, "Process_Data_Transfer " & columnstr & vbCrLf & ex.Message, 0)
        End Try
    End Sub

    Public Function StartLog(Sdocument As String, TTable As String, SDocCount As Integer)
        Try
            objconnectionautohrdw.Open()
            SQLCommand = New Data.SqlClient.SqlCommand("sproc_save_logs", objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.StoredProcedure
            SQLCommand.Parameters.Add("@Reference", SqlDbType.NVarChar, 1000, "@Reference")
            SQLCommand.Parameters("@Reference").Value = Sdocument
            SQLCommand.Parameters.Add("@Destination", SqlDbType.NVarChar, 4000, "@Destination")
            SQLCommand.Parameters("@Destination").Value = TTable
            SQLCommand.Parameters.Add("@ReferenceDocumentCount", SqlDbType.Int, 4, "@ReferenceDocumentCount")
            SQLCommand.Parameters("@ReferenceDocumentCount").Value = SDocCount
            SQLReader = SQLCommand.ExecuteReader(Data.CommandBehavior.CloseConnection)

            If SQLReader.Read Then
                Lockid = SQLReader("LockID")
            End If

            objconnectionautohrdw.Close()

        Catch ex As Exception
            objconnectionautohrdw.Close()
        End Try
        Return 0
    End Function

    Public Sub EndLog(lckid As Integer, DescCount As Integer)
        Try
            objconnectionautohrdw.Open()
            SQLCommand = New Data.SqlClient.SqlCommand("sproc_save_endlogs", objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.StoredProcedure
            SQLCommand.Parameters.Add("@DestinationRowsCount", SqlDbType.Int, 4, "@DestinationRowsCount")
            SQLCommand.Parameters("@DestinationRowsCount").Value = lckid
            SQLCommand.Parameters.Add("@LockID", SqlDbType.Int, 4, "@LockID")
            SQLCommand.Parameters("@LockID").Value = DescCount
            SQLCommand.ExecuteNonQuery()
            objconnectionautohrdw.Close()
        Catch ex As Exception
            objconnectionautohrdw.Close()
        End Try
    End Sub

    Public Function Get_Detail_Table_Fields(TargetDetailedTable As String)
        Dim retval = New List(Of String)

        Try
            objconnectionautohrdw.Open()
            SQLCommand = New Data.SqlClient.SqlCommand("sproc_get_table_details_fields", objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.StoredProcedure
            SQLCommand.Parameters.Add("@Target_Table", SqlDbType.NVarChar, 50, "@Target_Table")
            SQLCommand.Parameters("@Target_Table").Value = TargetDetailedTable
            SQLReader = SQLCommand.ExecuteReader(Data.CommandBehavior.CloseConnection)

            While SQLReader.Read
                retval.Add(SQLReader("FieldName"))
            End While
            objconnectionautohrdw.Close()

        Catch ex As Exception
            objconnectionautohrdw.Close()
        End Try

        Return retval
    End Function

    Public Sub Desrialized_Json(jsonfile As String, refid As String, TargetTable As String)
        Try
            Dim ResultTable As DataTable = Newtonsoft.Json.JsonConvert.DeserializeObject(Of DataTable)(jsonfile)
            Dim newColumn As New Data.DataColumn("ReferenceID", GetType(System.String))
            newColumn.DefaultValue = refid
            ResultTable.Columns.Add(newColumn)
            Process_Data_Transfer(TargetTable, ResultTable)
        Catch ex As Exception
            StartLog(TargetTable, refid & vbCrLf & ex.Message & vbCrLf & jsonfile, 0)
        End Try
    End Sub

    Public Function ToJson(ByVal bson As BsonDocument) As String
        Using stream = New MemoryStream()
            Using writer = New BsonBinaryWriter(stream)
                BsonSerializer.Serialize(writer, GetType(BsonDocument), bson)
            End Using

            stream.Seek(0, SeekOrigin.Begin)

            Using reader = New Newtonsoft.Json.Bson.BsonReader(stream)
                Dim sb = New StringBuilder()
                Dim sw = New StringWriter(sb)

                Using jWriter = New JsonTextWriter(sw)
                    jWriter.DateTimeZoneHandling = DateTimeZoneHandling.Utc
                    jWriter.WriteToken(reader)
                End Using

                Return sb.ToString()
            End Using
        End Using
    End Function

    Private Sub DataGridView1_CellContentClick(sender As Object, e As DataGridViewCellEventArgs) Handles DataGridView1.CellContentClick
    End Sub

End Class