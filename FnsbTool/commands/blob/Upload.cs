using System.CommandLine;
using System.CommandLine.Invocation;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FnsbTool.commands.blob;

public class Upload : Command
{
    public Upload()
        : base("upload", "Upload file to azure blob storage")
    {
        AddOptions(this);
    }

    public class CommandHandler(
        ILogger<CommandHandler> logger,
        IConfiguration configuration,
        BlobServiceClient blobServiceClient
    ) : ICommandHandler
    {
        public required FileInfo File { get; set; }
        public required string AccountName { get; set; } = blobServiceClient.AccountName;
        public required string? DeleteOlderPrefix { get; set; }
        public string? Container { get; set; }

        public int Invoke(InvocationContext context)
        {
            List<BlobItem> blobsToDelete = [];
            var container = blobServiceClient.GetBlobContainerClient(Container);
            if (!string.IsNullOrEmpty(DeleteOlderPrefix))
            {
                blobsToDelete.AddRange(
                    container.GetBlobs(prefix: DeleteOlderPrefix).AsEnumerable()
                );
            }

            var blobClient = blobServiceClient
                .GetBlobContainerClient(Container)
                .GetBlobClient(File.Name);

            blobClient.Upload(File.Open(FileMode.Open), true);

            foreach (var blob in blobsToDelete)
            {
                logger.LogInformation($"Deleted {blob.Name}");
                container.DeleteBlob(blob.Name);
            }

            Console.WriteLine(blobClient.Uri);

            return 0;
        }

        public Task<int> InvokeAsync(InvocationContext context)
        {
            return Task.FromResult(Invoke(context));
        }
    }

    private static void AddOptions(Command command)
    {
        var file = new Argument<FileInfo>("file", "The file to upload.")
        {
            Arity = ArgumentArity.ExactlyOne,
        };

        var deleteOlder = new Option<string>(
            "delete-older-prefix",
            "deletes older versions of the file with the given prefix"
        );
        command.AddArgument(file);
        command.AddOption(deleteOlder);
    }
}
