using FnsbTool.commands.database;

namespace FnsbTool.commands;

using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.Logging;

public class Database : Command
{
    public Database()
        : base("database", "Work with the property search database.")
    {
        AddCommand(new database.Export());
        AddCommand(new Import());
        AddOptions(this);
    }

    public class CommandHandler(ILogger<CommandHandler> logger) : ICommandHandler
    {
        public int Invoke(InvocationContext context)
        {
            return 1;
        }

        public Task<int> InvokeAsync(InvocationContext context)
        {
            return Task.FromResult(Invoke(context));
        }
    }

    static void AddOptions(Command command)
    {
        var hostname = new Option<string>("--hostname", "Hostname of database.");
        var port = new Option<string>("--port", "Port of database.");

        command.AddGlobalOption(hostname);
        command.AddGlobalOption(port);
    }
}
