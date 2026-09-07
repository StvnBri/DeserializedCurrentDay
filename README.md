# DeserializedCurrentDay

An incremental (current-day) ETL utility built in VB.NET that migrates transactional data from MongoDB to Microsoft SQL Server. Unlike a full-refresh migration, this tool only extracts and reloads records modified within a specific date window — designed for frequent, lightweight daily syncs of transactional collections from an ArcusAir/IQVIA HIS MongoDB source.

## Overview

This tool runs a targeted daily refresh cycle: it clears only the destination records matching a given date, pulls matching documents from MongoDB using date-based filters, and reloads them into SQL Server — keeping the transactional dataset up to date without reprocessing the entire history.

## Key Features

- **Date-Filtered Extraction** — Queries MongoDB collections using a configurable date filter field, limiting extraction to a specific day's transactions (defaults to the previous day).
- **Multi-Filter Support** — Supports up to three filter fields per source/target mapping, allowing a single collection to be queried against multiple date-tracked fields (e.g., created date, updated date, transaction date).
- **Targeted Cleanup** — Deletes only destination records matching the current processing date before reload, preserving historical data outside the window.
- **Reference & Detail Table Cleanup** — Removes both the main record and its related detail/child table rows by reference ID before reinserting fresh data, avoiding duplicates.
- **Row-by-Row BSON Flattening** — Converts MongoDB BSON documents into flat SQL-compatible rows for bulk transfer.
- **BSON-to-JSON Conversion Utility** — Includes a helper (`ToJson`) for converting BSON documents into JSON text using streaming writers.
- **JSON Deserialization Support** — Can also ingest raw JSON payloads directly into a target table, tagging each row with a reference ID.
- **Centralized Logging with Lock Tracking** — Logs start/end of each processing run (including row counts) to SQL Server, using a `LockID` returned from the logging stored procedure to correlate start and end entries.
- **Credential Management** — Retrieves the MongoDB connection string securely from SQL Server rather than hardcoding it.

## Core Components

| Component | Description |
|---|---|
| `Get_MongDB_Credentials` | Retrieves the MongoDB connection string from SQL Server |
| `Get_Source_Target` | Orchestrates the daily refresh across all configured source/target/filter combinations |
| `Clear_Destination` | Deletes destination rows matching the current processing date |
| `Extract_Data_From_MongoDB` | Pulls documents from MongoDB filtered by date, row by row |
| `Delete_Table_By_Reference` / `Delete_Detail_Table_By_ReferenceID` | Removes existing main and detail records by reference ID before reinsertion |
| `Process_Data_Transfer` | Bulk-inserts data into the destination SQL Server table |
| `LoadBsonDetail` | Parses raw BSON text blocks into rows for detail table loading |
| `Desrialized_Json` | Deserializes raw JSON into a DataTable and loads it into a target table |
| `ToJson` | Converts a BSON document into JSON text |
| `StartLog` / `EndLog` | Records the start (with row count) and end of each processing run, linked via `LockID` |
| `Get_Detail_Table_Fields` | Retrieves the expected field list for a given detail table from SQL Server metadata |

## Tech Stack

- VB.NET (WinForms)
- MongoDB.Driver / MongoDB.Bson
- Newtonsoft.Json (JSON/BSON conversion)
- ADO.NET (SqlClient) with SqlBulkCopy
- SQL Server (stored procedures for credentials, logging, and metadata)

## Difference from DeserializedApp (Full Refresh)

| | DeserializedApp | DeserializedCurrentDay |
|---|---|---|
| Scope | Full data refresh (all records) | Current/previous day only, date-filtered |
| Cleanup | Clears entire destination table | Clears only rows matching the processing date |
| Filters | None — pulls all documents | Up to 3 date filter fields per mapping |
| Use case | Reference/lookup tables | Transactional, frequently-changing data |

## Notes

- Requires a valid connection string configured under `connectionStringDW` in application settings.
- Source-to-target-to-filter mappings are managed via SQL Server metadata (`sproc_get_ArcusAir_Reference_Target_Transactional_CurrentDay`).
- Defaults to processing the previous day's data (`Now.AddDays(-1)`).
