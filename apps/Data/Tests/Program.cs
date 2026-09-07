using HavenOS.Apps.Data;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var address = new DataCellAddress("Sheet 1", 0, 1);
var snapshot = new DataCellSnapshot(address, "42", "=SUM(A1:A2)");
Assert(snapshot.Address.Sheet == "Sheet 1", "Cell address sheet was not retained.");
Assert(snapshot.Address.Row == 0 && snapshot.Address.Column == 1, "Cell coordinates were not retained.");
Assert(snapshot.Formula == "=SUM(A1:A2)", "Formula was not retained.");

var range = new DataRangeRequest("Sheet 1", 0, 0, 10, 8);
Assert(range.RowCount == 10 && range.ColumnCount == 8, "P1 grid shape changed unexpectedly.");

var queryResult = new DataQueryResult(["total"], [["42"]], false);
Assert(queryResult.Columns.Count == 1 && queryResult.Rows.Count == 1, "Query result contract is invalid.");

Console.WriteLine("Haven Data contract smoke checks passed.");
