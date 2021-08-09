using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Azure.Storage.Blobs;
using Azure.Identity;
using Azure.Core;
using System.Text;
using Microsoft.Azure.ServiceBus;
using System.Threading;
using Azure;
using System.Net.Http;

namespace OrderItemsReserver
{
    public static class OrderItemsReserver
    {
        const string ServiceBusConnectionString = "BusConnection";
        const string QueueName = "ordermessages";

        [FunctionName("OrderItemsReserver")]
        public static async Task Run(
            [ServiceBusTrigger(QueueName, Connection = ServiceBusConnectionString)] string req,
            ILogger log)
        {
            var count = 0;
            while (true)
            {
                try
                {
                    var container = new BlobContainerClient(
                     "DefaultEndpointsProtocol=https;AccountName=orderitems;AccountKey=MjvcEIbik7WmIhPGKbA7fJ6+KYGufvCghOJhLE6hBRPDcSB/6irZ7r2FnjSuFs7c/o00hh1Ywael9HXWJzvavQ==;EndpointSuffix=core.windows.net",
                     "orders");
                    var blob = container.GetBlobClient(Guid.NewGuid().ToString() + ".json");

                    byte[] byteArray = Encoding.UTF8.GetBytes(req);
                    MemoryStream stream = new MemoryStream(byteArray);

                    await blob.UploadAsync(stream);
                    break;
                }
                catch (RequestFailedException e)
                {
                    if (count > 3)
                    {
                        var httpClient = new HttpClient();
                        await httpClient.PostAsync(
                            "https://prod-240.westeurope.logic.azure.com:443/workflows/9e2bd13ffcce4f2abc04f31116f14ff9/triggers/manual/paths/invoke?api-version=2016-10-01&sp=%2Ftriggers%2Fmanual%2Frun&sv=1.0&sig=oo0gdE_XfY0pVMy9enjIvxQwPPKZdsTZ2QfwtOz0Jg8",
                             new StringContent(req));

                        break;
                    }
                    log.LogWarning($"Failed to write data to blob. number of try: {count + 1}");
                    await Task.Delay(1000);
                    count++;
                    continue;
                }
                catch (Exception e)
                {
                    log.LogError("Exception Message: " + e.Message);
                    break;
                }
            }
        }
    }
}
