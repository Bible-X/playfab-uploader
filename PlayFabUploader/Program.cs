using System;
using System.Diagnostics;
using System.Collections.Generic;
using PlayFab;
using PlayFab.MultiplayerModels;
using Azure.Storage.Blobs;

namespace PlayFabUploader
{
    class Program
    {
        static async System.Threading.Tasks.Task Main(string[] args)
        {

            if (args.Length < 6)
            {
                Console.WriteLine("Usage: playfabuploader.exe <SECRET KEY> <TITLE ID> <BUILD NAME> <LOCAL FILE PATH> <REMOTE FILE NAME>");
                return;
            }
            PlayFabSettings.staticSettings.DeveloperSecretKey = args[1];
            PlayFabSettings.staticSettings.TitleId = args[2];
            var buildName = args[3];
            var sourceFile = args[4];
            var targetFile = args[5];

            var tokenRes = await PlayFabAuthenticationAPI.GetEntityTokenAsync(new PlayFab.AuthenticationModels.GetEntityTokenRequest());
            if (tokenRes.Error != null)
            {
                Console.WriteLine(tokenRes.Error.ErrorMessage);
                return;
            }
            
            var buildSummaries = await PlayFabMultiplayerAPI.ListBuildSummariesV2Async(new ListBuildSummariesRequest());
            foreach (var b in buildSummaries.Result.BuildSummaries)
            {
                Console.WriteLine(b.BuildName);
            }

            var assetsUploadURLReq= new GetAssetUploadUrlRequest();
            assetsUploadURLReq.FileName = targetFile;

            var assetsUploadURLRes = await PlayFabMultiplayerAPI.GetAssetUploadUrlAsync(assetsUploadURLReq);
            Console.WriteLine(assetsUploadURLRes.Result.AssetUploadUrl);

            var blob = new BlobClient(new Uri(assetsUploadURLRes.Result.AssetUploadUrl));
            Console.WriteLine("Upload starting. This may take a while");

            Stopwatch sw = new Stopwatch();
            sw.Start();
            blob.Upload(sourceFile, true); // true is required because otherwise the client messes up and fails upload
            sw.Stop();

            Console.WriteLine("Upload finished. Took {0} seconds", sw.Elapsed);

            var buildSettings = new CreateBuildWithManagedContainerRequest();
            buildSettings.AreAssetsReadonly = false;
            buildSettings.GameAssetReferences = new List<AssetReferenceParams> { new AssetReferenceParams {
                FileName = targetFile,
                MountPath = "C:\\Assets",
            } };
            buildSettings.UseStreamingForAssetDownloads = true;
            buildSettings.BuildName = string.Format("Build {0}", buildName);
            buildSettings.VmSize = AzureVmSize.Standard_D2as_v4;
            buildSettings.ContainerFlavor = ContainerFlavor.ManagedWindowsServerCore;
            buildSettings.GameWorkingDirectory = "C:\\Assets";
            buildSettings.StartMultiplayerServerCommand = "C:\\Assets\\JerusalemEditorServer.exe";
            buildSettings.RegionConfigurations = new List<BuildRegionParams>{
                new BuildRegionParams
            {
                DynamicStandbySettings = new DynamicStandbySettings { IsEnabled = false },
                Region = "NorthEurope", // https://docs.microsoft.com/en-us/rest/api/playfab/multiplayer/multiplayerserver/createbuildwithprocessbasedserver?view=playfab-rest#azureregion
            }
            };
            buildSettings.MultiplayerServerCountPerVm = 1;
            buildSettings.Ports = new List<Port>{
                new Port {
                    Num = 7777,
                    Protocol = ProtocolType.UDP,
                    Name = "main",
                }
                };

            var buildRes = await PlayFabMultiplayerAPI.CreateBuildWithManagedContainerAsync(buildSettings);
            Console.WriteLine(buildRes.Result.BuildId);
        }
    }
}
