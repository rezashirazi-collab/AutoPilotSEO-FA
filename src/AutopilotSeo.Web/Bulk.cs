using System.Data;
using AutopilotSeo.Core;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AutopilotSeo.Web;

/// <summary>SqlBulkCopy over the DbContext's own connection: EF row-by-row inserts are too slow for fact tables over a VPN link.</summary>
public static class Bulk
{
    public static DataTable Table(params (string Name, Type Type)[] cols)
    {
        var t = new DataTable();
        foreach (var (n, ty) in cols) t.Columns.Add(n, Nullable.GetUnderlyingType(ty) ?? ty);
        return t;
    }

    public static async Task InsertAsync(AppDbContext db, string table, DataTable data, SqlTransaction? tx, CancellationToken ct)
    {
        if (data.Rows.Count == 0) return;
        await db.Database.OpenConnectionAsync(ct);
        using var bulk = new SqlBulkCopy((SqlConnection)db.Database.GetDbConnection(), SqlBulkCopyOptions.Default, tx)
        {
            DestinationTableName = table, BatchSize = 5000, BulkCopyTimeout = 300,
        };
        foreach (DataColumn c in data.Columns) bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
        await bulk.WriteToServerAsync(data, ct);
    }
}
