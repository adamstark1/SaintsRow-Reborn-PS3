using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using SaintsRowReborn.Hydra.DataTypes;

namespace SaintsRowReborn.Services;

public static class FirebaseIntegrationService
{
    private const string FirebaseProjectId = "saints-row-reborn";
    private const string FirebaseStorageBucket = "saints-row-reborn.firebasestorage.app";

    public static async Task UploadCharacterAsync(long charId, string charName, string psnName, string uploadTime, HydraHashMap metaMap, HydraBinary imgBinary)
    {
        try
        {
            using var httpClient = new HttpClient();
            string storageUrlBase = $"https://firebasestorage.googleapis.com/v0/b/{FirebaseStorageBucket}/o?name=";
            string firestoreUrl = $"https://firestore.googleapis.com/v1/projects/{FirebaseProjectId}/databases/(default)/documents/characters?documentId={charId}";

            var imgContent = new ByteArrayContent(imgBinary.Value);
            imgContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            var resImg = await httpClient.PostAsync($"{storageUrlBase}Characters%2F{charId}.jpg", imgContent);
            if (!resImg.IsSuccessStatusCode) Console.WriteLine($"[IMG ERROR] {await resImg.Content.ReadAsStringAsync()}");

            using var binMs = new MemoryStream();
            metaMap.Serialize(binMs);
            var binContent = new ByteArrayContent(binMs.ToArray());
            binContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            var resBin = await httpClient.PostAsync($"{storageUrlBase}Characters%2F{charId}.sr_character", binContent);
            if (!resBin.IsSuccessStatusCode) Console.WriteLine($"[BIN ERROR] {await resBin.Content.ReadAsStringAsync()}");
            
            var firestoreDocument = new
            {
                fields = new
                {
                    CharacterId = new { stringValue = charId.ToString() },
                    Name = new { stringValue = charName },
                    PsnOnlineId = new { stringValue = psnName },
                    UploadedAt = new { stringValue = uploadTime },
                    ImageUrl = new { stringValue = $"https://firebasestorage.googleapis.com/v0/b/{FirebaseStorageBucket}/o/Characters%2F{charId}.jpg?alt=media" },
                    DataUrl = new { stringValue = $"https://firebasestorage.googleapis.com/v0/b/{FirebaseStorageBucket}/o/Characters%2F{charId}.sr_character?alt=media" }
                }
            };

            string jsonString = JsonSerializer.Serialize(firestoreDocument);
            var jsonContent = new StringContent(jsonString, System.Text.Encoding.UTF8, "application/json");
            var resDb = await httpClient.PostAsync(firestoreUrl, jsonContent);
            if (!resDb.IsSuccessStatusCode) Console.WriteLine($"[DB ERROR] {await resDb.Content.ReadAsStringAsync()}");

            if (resImg.IsSuccessStatusCode && resBin.IsSuccessStatusCode && resDb.IsSuccessStatusCode)
            {
                Console.WriteLine($"[UGC] Character uploaded to Reborn Gallery: {charId} by {psnName}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UGC ERROR] {ex.Message}");
        }
    }
}