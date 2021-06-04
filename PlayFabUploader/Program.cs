using System;
using System.Diagnostics;
using System.Collections.Generic;
using PlayFab;
using PlayFab.MultiplayerModels;
using Azure.Storage.Blobs;
using System.Text.Json;


namespace PlayFabUploader
{

    public class MultiplayerServerData
    {
        public string BuildID { get; set; }
        public string Region { get; set; }
        public string SessionID { get; set; }
    }

    class Program
    {
        public const string MULTIPLAYER_SERVER_DATA = "MultiplayerServerData";

        static async System.Threading.Tasks.Task Main(string[] args)
        {
            if (args.Length < 5)
            {
                Console.WriteLine("Usage: playfabuploader.exe <SECRET KEY> <TITLE ID> <BUILD NAME> <LOCAL FILE PATH> <REMOTE FILE NAME>");
                return;
            }

            PlayFabSettings.staticSettings.DeveloperSecretKey = args[0];
            PlayFabSettings.staticSettings.TitleId = args[1];
            var buildName = args[2];
            var sourceFile = args[3];
            var targetFile = args[4];

            var tokenRes = await PlayFabAuthenticationAPI.GetEntityTokenAsync(new PlayFab.AuthenticationModels.GetEntityTokenRequest());
            if (tokenRes.Error != null)
            {
                Console.WriteLine(tokenRes.Error.ErrorMessage);
                return;
            }

            var assetsUploadURLReq = new GetAssetUploadUrlRequest();
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
                }
            };

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
                    StandbyServers = 0,
                    MaxServers = 1,
                    ScheduledStandbySettings = new ScheduledStandbySettings
                    {
                        IsEnabled = false,
                    }
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
            Console.WriteLine("Created build ID: {0}", buildRes.Result.BuildId);

            Console.WriteLine("Setting build id in MultiplayerServerData");
            var titleDataRes = await PlayFabServerAPI.GetTitleDataAsync(new PlayFab.ServerModels.GetTitleDataRequest
            {
                Keys = new List<string>
                {
                    MULTIPLAYER_SERVER_DATA,
                }
            });

            titleDataRes.Result.Data.TryGetValue(MULTIPLAYER_SERVER_DATA, out string mpdJson);
            var mpd = JsonSerializer.Deserialize<MultiplayerServerData>(mpdJson);
            mpd.BuildID = buildRes.Result.BuildId;
            mpdJson = JsonSerializer.Serialize(mpd);

            await PlayFabServerAPI.SetTitleDataAsync(new PlayFab.ServerModels.SetTitleDataRequest
            {
                Key = MULTIPLAYER_SERVER_DATA,
                Value = mpdJson,
            });


        }
    }
}
