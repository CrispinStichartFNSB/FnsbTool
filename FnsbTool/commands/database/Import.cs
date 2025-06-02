using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text;
using Dapper;
using FnsbTool.shared;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace FnsbTool.commands.database;

public class Import : Command
{
    public Import()
        : base("import", "Import data into a database.")
    {
        AddOptions(this);
    }

    public class CommandHandler(ILogger<CommandHandler> logger, SqlConnection connection)
        : ICommandHandler
    {
        public string? InFile { get; set; }
        public string? TableName { get; set; }

        public Verbosity Verbosity { get; set; } = Verbosity.Normal;
        public bool BinaryFormat { get; set; } = false;
        public string ColumnSeparator { get; set; } = " ";
        public string RowSeparator { get; set; } = "\n";
        public bool DropExisting { get; set; } = false;

        public int Invoke(InvocationContext context)
        {
            if (BinaryFormat)
            {
                ColumnSeparator = "\u001F";
                RowSeparator = "\u001E\n";
            }

            connection.Open();

            if (DropExisting)
            {
                var truncateSql = $"TRUNCATE TABLE {TableName}";
                connection.Execute(truncateSql);
            }

            using var transaction = connection.BeginTransaction();

            // 1. discover the column order of the target table
            var columnNames = connection
                .Query<string>(
                    @"SELECT name
            FROM sys.columns
           WHERE object_id = OBJECT_ID(@tbl)
        ORDER BY column_id;",
                    new { tbl = TableName },
                    transaction
                )
                .ToArray();

            if (columnNames.Length == 0)
            {
                logger.LogError("Table '{TableName}' not found or has no columns.", TableName);
                return 1;
            }

            // 2. build one parameterised INSERT statement, re‑using it for every row
            var colList = string.Join(", ", columnNames.Select(n => $"[{n}]"));
            var paramList = string.Join(", ", columnNames.Select((_, i) => $"@p{i}"));

            var insertSql = $"""
                INSERT INTO {TableName} ({colList})
                VALUES ({paramList});
                """;

            // 3. stream the file and convert each row into a DynamicParameters object
            IEnumerable<string> ReadRows()
            {
                if (RowSeparator == "\n") // common case: plain text
                {
                    using var sr = new StreamReader(InFile!, Encoding.UTF8);
                    while (sr.ReadLine() is { } line)
                        yield return line;
                }
                else // custom (possibly binary) row delimiter
                {
                    var content = File.ReadAllText(InFile!, Encoding.UTF8);
                    foreach (
                        var row in content.Split(
                            RowSeparator,
                            StringSplitOptions.RemoveEmptyEntries
                        )
                    )
                        yield return row;
                }
            }

            const int batchSize = 1_000;
            var batch = new List<DynamicParameters>(batchSize);

            foreach (var row in ReadRows())
            {
                var values = row.Split(ColumnSeparator);

                var parameters = new DynamicParameters();
                for (var i = 0; i < columnNames.Length; i++)
                {
                    object? value = i < values.Length ? values[i] : null;

                    // convert \N -> NULL in binary mode
                    // TODO: check if this needs to use DbNull
                    if (BinaryFormat && value is "\\N")
                        value = null;

                    parameters.Add($"p{i}", value);
                }

                batch.Add(parameters);

                if (batch.Count < batchSize)
                    continue;

                connection.Execute(insertSql, batch, transaction);
                batch.Clear();
            }

            // flush any leftover rows
            if (batch.Count > 0)
                connection.Execute(insertSql, batch, transaction);

            transaction.Commit();
            return 0;
        }

        public Task<int> InvokeAsync(InvocationContext context)
        {
            return Task.FromResult(Invoke(context));
        }
    }

    static void AddOptions(Command command)
    {
        command.AddOption(
            new Option<string>(["-i", "--infile"], "The data to import") { IsRequired = true }
        );
        command.AddOption(
            new Option<string>(
                "--column-separator",
                "The character or string used to separate columns."
            )
        );
        command.AddOption(
            new Option<string>("--row-separator", "The character or string used to separate rows.")
        );
        command.AddOption(
            new Option<bool>("--binary-format", "Use \\N to mark nulls and unit/record separators.")
        );
        command.AddOption(
            new Option<bool>("--drop-existing", "Drops the existing data before importing.")
        );

        command.AddArgument(
            new Argument<string>("table-name", "Name of table to import data into")
            {
                Arity = ArgumentArity.ExactlyOne,
            }
        );
    }
}
