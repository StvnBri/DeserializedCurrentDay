'-----------------------------------
' UAT Prescription
'----------------------------


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
    Public FilterField1 As String
    Public FilterField2 As String
    Public FilterField3 As String
    Dim customdate As Date

    ' Variables for Timer
    Private etlTimer As Stopwatch
    Private breakSecondsRemaining As Integer = 0
    Private Const BREAK_DURATION As Integer = 300000 ' 5 minutes (in seconds)
    Private isRunning As Boolean = False
    Private nextRunTime As DateTime


    'runtime timer
    Private Sub Timer1_Tick(sender As Object, e As EventArgs) Handles Timer1.Tick
        If etlTimer IsNot Nothing AndAlso etlTimer.IsRunning Then
            Lbl_runtime_stopwatch.Text = etlTimer.Elapsed.ToString("hh\:mm\:ss")
        End If
    End Sub

    Private Sub Timer2_Tick(sender As Object, e As EventArgs) Handles Timer2.Tick

        Dim remaining As TimeSpan = nextRunTime - DateTime.Now

        If remaining.TotalSeconds > 0 Then
            Lbl_break_stopwatch.Text = remaining.ToString("mm\:ss")
        Else
            Timer2.Stop()

            Lbl_break_stopwatch.Text = "00:00"
            Lbl_status.Text = "Starting ETL..."

            isRunning = False
            RunETL() '  restart
        End If

    End Sub
    Private Async Sub RunETL()

        If isRunning Then Exit Sub

        isRunning = True

        Try
            ' RESET UI
            Lbl_break_stopwatch.Text = "00:00"
            Lbl_status.Text = "Processing..."

            ' START runtime
            etlTimer = Stopwatch.StartNew()
            Timer1.Start()

            ' RUN ETL in background
            Await Task.Run(Sub()
                               Get_MongDB_Credentials()
                               Get_Source_Target()
                           End Sub)

            ' STOP runtime
            etlTimer.Stop()
            Timer1.Stop()

            Lbl_runtime_stopwatch.Text = etlTimer.Elapsed.ToString("hh\:mm\:ss")

            ' START BREAK
            Lbl_status.Text = "Break..."
            nextRunTime = DateTime.Now.AddMinutes(15)
            'breakSecondsRemaining = BREAK_DURATION
            Timer2.Start()

        Catch ex As Exception

            Timer1.Stop()
            If etlTimer IsNot Nothing Then etlTimer.Stop()

            Lbl_status.Text = "Error"
            isRunning = False

            MsgBox(ex.Message)

        End Try

    End Sub


    Private Sub frmdeserialized_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        StartLog("DATA MIGRATION", "START", 1)
        RunETL()
    End Sub


    'Private Sub frmmain_Load(sender As Object, e As EventArgs) Handles MyBase.Load
    '    Get_MongDB_Credentials()
    '    Get_Source_Target()
    '    End
    'End Sub
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

        etlTimer = Stopwatch.StartNew()

        Try
            'customdate = InputBox("enterdate mm/dd/yyyy")
            ' customdate = Now.AddDays(-1).ToShortDateString
            ' customdate = Now.ToShortDateString
            customdate = "01/01/2000" ' For Testing only.
            objconnectionautohrdwLoop.Open()
            SQLCommandLoop = New Data.SqlClient.SqlCommand("sproc_get_ArcusAir_Reference_Target_Transactional_CurrentDay", objconnectionautohrdwLoop)
            SQLCommandLoop.CommandType = CommandType.StoredProcedure
            SQLReaderLoop = SQLCommandLoop.ExecuteReader(Data.CommandBehavior.CloseConnection)
            Do While SQLReaderLoop.Read
                SourceDocument = SQLReaderLoop("Source_Document") ' where it came from/Source
                TargetTable = SQLReaderLoop("Target_Table") ' where to write it/ Destination
                FilterField1 = SQLReaderLoop("Filter1")
                Try
                    FilterField2 = SQLReader("Filter2")
                Catch ex As Exception
                    FilterField2 = ""
                End Try
                Try
                    FilterField3 = SQLReader("Filter3") & ""
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
            Loop
            Execute_Array_Metadata_Phase()
            objconnectionautohrdwLoop.Close()

            ' New
            load_prescription()

            etlTimer.Stop()

            Dim totalTime As String = etlTimer.Elapsed.ToString("hh\:mm\:ss")

            StartLog("ETL_RUNTIME", "Total Runtime: " & totalTime, 0)

            Return

        Catch ex As Exception
            objconnectionautohrdwLoop.Close()

            etlTimer.Stop()

            Dim totalTime As String = etlTimer.Elapsed.ToString("hh\:mm\:ss")

            StartLog("ETL_RUNTIME_ERROR", "Runtime: " & totalTime & vbCrLf & ex.Message, 0)

            Return

            'StartLog("Get_Source_Target " & SourceDocument, ex.Message, 0)
            ' End
        End Try
    End Sub
    Public Sub Clear_Destination(ReferenceTbl As String, RefField As String)
        Try
            'querystring = "Delete From " & ReferenceTbl & " where CONVERT(date, " & RefField & ") = '" & Now.ToShortDateString & "'"
            querystring = "Delete From " & ReferenceTbl & " where CONVERT(date, " & RefField & ") = '" & customdate & "'"
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
        Dim lcnt As Integer
        Dim vcnt As Integer
        Dim dt As New DataTable
        Dim tempstr As String = ""
        Dim dr As DataRow
        Dim mongo As MongoClient = New MongoClient(mongodbstr)
        Dim db = mongo.GetDatabase("arcusairdb")
        Dim collection = db.GetCollection(Of BsonDocument)(SDocument)
        Dim q = New BsonDocument()
        'Dim startDate As DateTime = New DateTime(Now.Year, Now.Month, Now.Day) '
        Dim startDate As DateTime = New DateTime(customdate.Year, customdate.Month, customdate.Day)
        Dim f = Builders(Of BsonDocument).Filter.And(Builders(Of BsonDocument).Filter.Gte(Of Date)(Filter, startDate))
        Dim list = collection.Find(f).ToList
        StartLog(SDocument, TTable, list.Count)
        Do Until lcnt = list.Count
            dt.Rows.Add()
            vcnt = 0
            Do Until vcnt = list.Item(lcnt).Values.Count
                Try
                    vcnt = vcnt + 1
                    Dim columnName As String = list.Item(lcnt).ElementAt(vcnt - 1).Name.ToString
                    If columnName <> "__v" Then
                        dt.Columns.Add(columnName, GetType(String))
                        dt.Rows(0)(columnName) = list.Item(lcnt).Values(vcnt - 1).ToString
                    End If
                Catch ex As Exception
                    StartLog("Extract_Data_From_MongoDB", SDocument & vbCrLf & ex.Message, 0)
                End Try
            Loop
            Delete_Recursive(TTable, dt.Rows.Item(0).Item("_id")) ' Delete 
            'Delete_Table_By_Reference(TTable, dt.Rows.Item(0).Item("_id"))
            'Delete_Detail_Table_By_ReferenceID(TTable, dt.Rows.Item(0).Item("_id"))
            Process_Data_Transfer(TTable, dt)
            dt.Rows.Clear()
            dt.Columns.Clear()
            lcnt = lcnt + 1
        Loop
        EndLog(Lockid, vcnt)
    End Sub
    Public Sub LoadBsonDetail(RefID As String, bsonfile As String, TargetTable As String)
        Dim lcnt As Integer
        Dim vcnt As Integer
        Dim dt As New DataTable
        Dim ds As New BindingSource
        Dim tempstr As String = ""
        Dim dr As DataRow
        Dim doccount As Integer
        Dim rawjson = bsonfile
        Dim separatingChars As String() = {"}, {"}
        Dim docs As String() = rawjson.Split(separatingChars, System.StringSplitOptions.RemoveEmptyEntries)
        ' TextBox2.Text = ""
        Try
            For Each doc As String In docs
                doccount = doccount + 1
                Try
                    'TextBox2.Text = TextBox2.Text & vbCrLf & BsonDocument.Parse("{" & doc.TrimEnd("}").TrimStart("{") & "}").ToString
                    Dim list = BsonDocument.Parse("{" & doc.TrimEnd("}").TrimStart("{") & "}").ToList
                    'TextBox2.Text = TextBox2.Text & vbCrLf & BsonDocument.Parse(doc).ToString
                    'Dim list = BsonDocument.Parse(doc).ToList
                    dt.Rows.Add()
                    vcnt = 0
                    dt.Columns.Add("ReferenceID", GetType(String))
                    dt.Rows(0)("ReferenceID") = RefID
                    Do Until vcnt = list.Count
                        Try
                            vcnt = vcnt + 1
                            Dim columnName As String = list.ElementAt(vcnt - 1).Name.ToString()
                            If columnName <> "__v" Then
                                dt.Columns.Add(columnName, GetType(String))
                                dt.Rows(0)(columnName) = list.Item(vcnt - 1).Value.ToString
                            End If
                        Catch ex2 As Exception
                            MsgBox(ex2.Message)
                        End Try
                    Loop
                Catch ex As Exception
                    doc = doc.Replace("} }", "}")
                    ' TextBox2.Text = TextBox2.Text & vbCrLf & BsonDocument.Parse("{" & doc & "}").ToString
                    Dim List = BsonDocument.Parse("{" & doc & "}").ToList
                    dt.Rows.Add()
                    vcnt = 0
                    dt.Columns.Add("ReferenceID", GetType(String))
                    dt.Rows(0)("ReferenceID") = RefID
                    Do Until vcnt = List.Count
                        Try
                            vcnt = vcnt + 1
                            Dim columnName As String = List.ElementAt(vcnt - 1).Name.ToString()
                            If columnName <> "__v" AndAlso columnName <> "end" Then
                                dt.Columns.Add(columnName, GetType(String))
                                dt.Rows(0)(columnName) = List.Item(vcnt - 1).Value.ToString
                            End If
                        Catch ex3 As Exception
                            MsgBox(ex3.Message)
                        End Try
                    Loop
                End Try
                Process_Data_Transfer(TargetTable, dt)
                dt.Rows.Clear()
                dt.Columns.Clear()
            Next
        Catch ex As Exception
            StartLog(TargetTable, RefID & ex.Message, 0)
        End Try
    End Sub
    Public Sub Delete_Table_By_Reference(TargetTable As String, TableReferenceID As String)
        Try
            querystring = "Delete From " & TargetTable & " where _id = '" & TableReferenceID & "'"
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
                querystring = "Delete From " & dtdetailtable.Rows(detailcount)(1).ToString & " where ReferenceID = '" & TableReferenceID & "'"
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


    Public Sub Delete_Recursive(MainTable As String, ReferenceID As String)
        Dim dtChild As New DataTable

        Try
            ' Get child tables of this parent
            If objconnectionautohrdw.State = ConnectionState.Open Then
                objconnectionautohrdw.Close()
            End If

            objconnectionautohrdw.Open()

            SQLCommand = New SqlClient.SqlCommand("sproc_get_main_detail_table", objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.StoredProcedure
            SQLCommand.Parameters.Clear()
            SQLCommand.Parameters.AddWithValue("@Main_Table", MainTable)

            SQLReader = SQLCommand.ExecuteReader()
            dtChild.Load(SQLReader)
            objconnectionautohrdw.Close()

            ' LOOP child tables
            For Each row As DataRow In dtChild.Rows

                Dim childTable As String = row("Detail_Table").ToString()

                '  RECURSION: delete deeper levels first
                Delete_Recursive(childTable, ReferenceID)

                ' DELETE child records
                Dim query As String = "DELETE FROM " & childTable & " WHERE reference_id = @refid"

                objconnectionautohrdw.Open()
                Dim cmd As New SqlClient.SqlCommand(query, objconnectionautohrdw)
                cmd.Parameters.AddWithValue("@refid", ReferenceID)
                cmd.ExecuteNonQuery()
                objconnectionautohrdw.Close()

            Next

            '  delete parent
            Dim parentQuery As String = "DELETE FROM " & MainTable & " WHERE _id = @refid"

            objconnectionautohrdw.Open()
            Dim parentCmd As New SqlClient.SqlCommand(parentQuery, objconnectionautohrdw)
            parentCmd.Parameters.AddWithValue("@refid", ReferenceID)
            parentCmd.ExecuteNonQuery()
            objconnectionautohrdw.Close()

        Catch ex As Exception
            If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
            StartLog("Delete_Recursive", MainTable & vbCrLf & ex.Message, 0)
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
            ' MsgBox(ex.Message)
            StartLog(sourcetablename, "Process_Data_Transfer " & columnstr & vbCrLf & ex.Message, 0)
            'End
        End Try
    End Sub
    ' Logic that finds all those parent table that have an array fields.
    Public Sub Execute_Array_Metadata_Phase()
        Dim dtMain As New DataTable ' will store main tables retrieved from SQL Server sp_get_main
        Dim dtDetail As New DataTable 'will store detail tables / array fields associated with each main table (sp_get_detail)
        Try
            If objconnectionautohrdw.State = ConnectionState.Open Then
                objconnectionautohrdw.Close()
            End If
            objconnectionautohrdw.Open()
            SQLCommand = New SqlClient.SqlCommand("sp_get_main", objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.StoredProcedure
            ' SQLCommand.CommandTimeout = 0
            SQLReader = SQLCommand.ExecuteReader()
            dtMain.Load(SQLReader)
            objconnectionautohrdw.Close()
            ' Loop through each Main Table
            For Each mainRow As DataRow In dtMain.Rows
                Dim mainTable As String = mainRow("Main_Table").ToString()
                Dim parentKeyColumn As String = If(mainRow.Table.Columns.Contains("Parent_Key_Column"), mainRow("Parent_Key_Column").ToString(), "")
                ' Get Detail Tables / Array Fields
                dtDetail.Clear()
                objconnectionautohrdw.Open()
                SQLCommand = New SqlClient.SqlCommand("sp_get_detail", objconnectionautohrdw)
                SQLCommand.CommandType = CommandType.StoredProcedure
                SQLCommand.Parameters.AddWithValue("@Main_Table", mainTable)
                SQLReader = SQLCommand.ExecuteReader()
                dtDetail.Load(SQLReader)
                objconnectionautohrdw.Close()
                For Each detailRow As DataRow In dtDetail.Rows
                    Dim detailTable As String = detailRow("Detail_Table").ToString()
                    Dim fieldName As String = detailRow("Field_Name").ToString()
                    Dim refId As String = "reference_id"
                    parentKeyColumn = "_id"
                    Process_Array_Migration(mainTable, detailTable, parentKeyColumn, fieldName, refId)
                Next
            Next
        Catch ex As Exception
            If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
            StartLog("ARRAY_PHASE", ex.Message, 0)
        End Try
    End Sub
    Public Sub Process_Array_Migration(mainTable As String, detailTable As String, parentKeyColumn As String, fieldName As String, refId As String)
        'FileLogger.WriteLog($"START ARRAY MIGRATION | Main={mainTable}, Detail={detailTable}, Field={fieldName}")
        Dim resultTables As New Dictionary(Of String, DataTable)
        Try
            Dim dtArray As New DataTable
            objconnectionautohrdw.Open()
            SQLCommand = New SqlClient.SqlCommand("sproc_get_array_data", objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.StoredProcedure
            SQLCommand.Parameters.AddWithValue("@ParentTable", mainTable)
            SQLCommand.Parameters.AddWithValue("@DetailTable", detailTable)
            SQLCommand.Parameters.AddWithValue("@ParentIdColumn", parentKeyColumn)
            SQLCommand.Parameters.AddWithValue("@FieldName", fieldName)
            SQLCommand.Parameters.AddWithValue("@RefId", refId)
            SQLReader = SQLCommand.ExecuteReader()
            dtArray.Load(SQLReader)
            objconnectionautohrdw.Close()
            If dtArray.Rows.Count = 0 Then Exit Sub
            For Each row As DataRow In dtArray.Rows
                Dim parentId As String = row("reference_id").ToString()
                Dim raw As String = row("ArrayField").ToString().Trim()
                If raw = "" OrElse raw = "0" Then
                    Continue For
                End If
                Dim bsonValue As BsonValue
                Try
                    bsonValue = MongoDB.Bson.Serialization.BsonSerializer _
                    .Deserialize(Of BsonValue)(raw)
                Catch ex As Exception
                    'FileLogger.WriteLog($"Invalid BSON skipped: {raw}")
                    Continue For
                End Try
                Recursive_Function(bsonValue, parentId, detailTable, resultTables)
            Next
            ' ---- BULK INSERT RESULTS ----
            For Each kvp In resultTables
                Array_Bulk_Insert(kvp.Key, kvp.Value)
                ' FileLogger.WriteLog($"Inserted {kvp.Value.Rows.Count} rows into {kvp.Key}")
            Next
            'StartLog(fieldName, detailTable, resultTables.Sum(Function(x) x.Value.Rows.Count))
            For Each kvp In resultTables
                StartLog(fieldName, kvp.Key, kvp.Value.Rows.Count)
            Next
        Catch ex As Exception
            If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
            StartLog(detailTable, ex.Message, 0)
            'FileLogger.WriteError(ex, "Process_Array_Migration.log")
        End Try
    End Sub
    ' RECURSIVE
    Private Sub Recursive_Function(value As BsonValue, parentId As String, tableName As String, resultTables As Dictionary(Of String, DataTable))
        If value Is Nothing OrElse value.IsBsonNull Then Exit Sub ' check is bson is null value or nothing
        ' ---- ARRAY ---- Check if the bson is array then iterate through each element and recursively process
        If value.IsBsonArray Then
            For Each item In value.AsBsonArray
                Recursive_Function(item, parentId, tableName, resultTables)
            Next
            Exit Sub
        End If
        ' ---- DOCUMENT ----
        If value.IsBsonDocument Then
            If Not resultTables.ContainsKey(tableName) Then
                resultTables(tableName) = CreateDynamicTable(tableName)
            End If
            Dim row As DataRow = resultTables(tableName).NewRow()
            row("reference_id") = parentId
            For Each el In value.AsBsonDocument.Elements
                If el.Name <> "__v" Then
                    If el.Value.IsBsonArray OrElse el.Value.IsBsonDocument Then
                        ' recursion
                        Recursive_Function(el.Value, parentId, tableName & "_" & el.Name, resultTables)
                    Else
                        If Not resultTables(tableName).Columns.Contains(el.Name) Then
                            resultTables(tableName).Columns.Add(el.Name, GetType(String))
                        End If
                        row(el.Name) = el.Value.ToString()
                    End If
                End If
            Next
            resultTables(tableName).Rows.Add(row)
        End If
    End Sub
    ' Creation of a temporary in-memory table to hold flattened array elements before bulk inserting into SQL.
    Private Function CreateDynamicTable(tableName As String) As DataTable
        Dim dt As New DataTable(tableName)
        dt.Columns.Add("reference_id", GetType(String))
        Return dt
    End Function
    Private Sub Array_Bulk_Insert(tableName As String, dt As DataTable)
        Dim columnstr As String
        Try
            Using bulk As New SqlClient.SqlBulkCopy(objconnectionautohrdw)
                bulk.DestinationTableName = tableName
                bulk.BatchSize = 5000
                bulk.BulkCopyTimeout = 0
                bulk.EnableStreaming = True
                For Each c As DataColumn In dt.Columns
                    bulk.ColumnMappings.Add(c.ColumnName, "[" & c.ColumnName & "]")
                    columnstr = columnstr & "," & c.ColumnName
                Next
                If objconnectionautohrdw.State <> ConnectionState.Open Then
                    objconnectionautohrdw.Open()
                End If
                bulk.WriteToServer(dt)
                objconnectionautohrdw.Close()
            End Using
        Catch ex As Exception
            If objconnectionautohrdw.State = ConnectionState.Open Then
                objconnectionautohrdw.Close()
            End If
            StartLog(tableName, columnstr & vbCrLf & ex.Message & vbCrLf & "Array_Bulk_Insert Error", 0)
            'FileLogger.WriteLog($"Error during Array_Bulk_Insert for {tableName}: {ex.Message}")
        End Try
    End Sub

    ' Loading view_prescription to Tbl_prescription
    Public Sub load_prescription()

        ' StartLog("LOADING PRESCRIPTION", "START", 0)

        Try
            StartLog("LOADING PRESCRIPTION", "START", 0)
            If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
            objconnectionautohrdw.Open()

            SQLCommand = New SqlClient.SqlCommand("sp_load_prescription", objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.StoredProcedure
            SQLCommand.ExecuteNonQuery()

            StartLog("LOADING PRESCRIPTION", "FINISH", 1)

        Catch ex As Exception
            objconnectionautohrdw.Close()
            StartLog("Loading view to table", ex.Message, 0)
        End Try
    End Sub
    Public Function StartLog(Sdocument As String, TTable As String, SDocCount As Integer)
        Try
            objconnectionautohrdw.Open()
            SQLCommand = New Data.SqlClient.SqlCommand("sproc_save_logs", objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.StoredProcedure
            SQLCommand.Parameters.Add("@Reference", SqlDbType.NVarChar, 50, "@Reference")
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
        '
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
            ' MsgBox(ex.Message)
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
            ' ResultTable.Columns.Add("ReferenceID", GetType(String))
            ' ResultTable.Rows(0)("ReferenceID") = refid
            Process_Data_Transfer(TargetTable, ResultTable)
        Catch ex As Exception
            ' MsgBox((ex.Message))
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