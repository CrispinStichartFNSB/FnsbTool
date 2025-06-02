using Dapper;
using FnsbTool.shared;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;

namespace FnsbTool.commands.database;

using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.Logging;

public enum ExportCommands
{
    Configuration,
    Properties,
    Cama,
}

public class Export : Command
{
    public static string Sections = """
        General, Land, Addresses, BillingAddress,
        FireServiceArea, ServiceAreas, Documents, TaxHistory,
        AssessmentHistory, Exemptions, TentativeValue,
        Structures, Children, Parents
        """;

    public Export()
        : base("export", "Export data from database.")
    {
        AddOptions(this);
    }

    public class CommandHandler(ILogger<CommandHandler> logger, SqlConnection connection)
        : ICommandHandler
    {
        public string? Outfile { get; set; }
        public int? TopX { get; set; }
        public Verbosity Verbosity { get; set; } = Verbosity.Normal;
        public bool BinaryFormat { get; set; } = false;
        public string ColumnSeparator { get; set; } = " ";
        public string RowSeparator { get; set; } = "\n";

        public ExportCommands ExportCommand { get; set; }

        public int Invoke(InvocationContext context)
        {
            if (BinaryFormat)
            {
                ColumnSeparator = "\u001F";
                RowSeparator = "\u001E\n";
            }

            string sql = ExportCommand switch
            {
                ExportCommands.Properties =>
                    $"""
                                              DECLARE @pans PropertySearch.PansList;
                                              INSERT @pans
                                              SELECT {(
                                                  TopX is not null ? $"TOP {TopX}" : string.Empty
                                              )} ppd_recordid FROM prop_phys_def;

                                              EXEC PropertySearch.usp_GetPropertyRecord_v2 @pans, N'{Sections};';
                                              """,
                ExportCommands.Configuration => "SELECT * FROM PropertySearch.Config",
                ExportCommands.Cama => "",
                _ => throw new ArgumentOutOfRangeException(),
            };

            // Resolve the output stream up‑front.
            var stream = !Outfile.IsNullOrEmpty()
                ? new FileStream(
                    Outfile!,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    1 << 20,
                    FileOptions.SequentialScan
                )
                : Console.OpenStandardOutput();

            // A large buffered writer amortises I/O calls.
            using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8, 1 << 16, false);

            writer.NewLine = "\n"; // stick to LF so we don’t have to translate.
            writer.AutoFlush = false; // we’ll flush explicitly on progress ticks / dispose.

            var rowCount = 0;

            connection.Open();
            using var reader = connection.ExecuteReader(sql);

            var columnCount = reader.FieldCount;
            var sb = new System.Text.StringBuilder(256); // will grow if necessary, but re‑used

            while (reader.Read())
            {
                sb.Clear();
                for (var i = 0; i < columnCount; i++)
                {
                    if (reader.IsDBNull(i) && BinaryFormat)
                    {
                        sb.Append(@"\N");
                    }
                    else
                    {
                        sb.Append(reader.GetValue(i));
                    }
                    // Append separator *except* after the last field.
                    if (i < columnCount - 1)
                        sb.Append(ColumnSeparator);
                }

                sb.Append(RowSeparator);

                writer.Write(sb);
                rowCount++;

                // Progress tick every 5000 records when writing to a file and verbosity >= Normal.
                if (Outfile is null || Verbosity < Verbosity.Normal || rowCount % 5000 != 0)
                {
                    continue;
                }

                writer.Flush(); // Push what we have so far.
                Console.Out.WriteLine($"{rowCount:N0} rows written");
            }

            writer.Flush(); // Final flush in case the buffer isn’t full.

            if (Outfile is not null && Verbosity >= Verbosity.Quiet)
            {
                Console.WriteLine($"Wrote {rowCount} lines to {Outfile}");
            }
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
            new Option<string>(["-o", "--outfile"], "writes output to the specified file")
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
        command.AddOption(new Option<int>("--top", "Export only the top X rows."));
        command.AddOption(
            new Option<bool>("--binary-format", "Use \\N to mark nulls and \\t for the separator")
        );

        command.AddArgument(
            new Argument<ExportCommands>("export-command") { Arity = ArgumentArity.ExactlyOne }
        );
    }
}
