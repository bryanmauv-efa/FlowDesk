using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TermServMultiScreen.Core;

public static class Updater
{
    private const string RepoOwner = "bryanmauv-efa";
    private const string RepoName = "FlowDesk";
    private const string PublicKeyXml = "<RSAKeyValue><Modulus>1swHaavtlU4hYmSPmoanvTgwOiJTN/VgzA/tKo4kLaYE//uUufhTWB/iuTvKx69vJqdJzVE3/R1efOjUPw1xBlPFnmWPbDMADSSFShqbtyjnUAPMvOtyf/qyQy6RmAbRZYdzI5LnkulFTeQw2nA0Hmio6lPKjX7OGtz7306CXyDkOvlJi2HE1qMzajvsQmXB0vbinhy14/VKQG3ktd2S2/UJJWXo9xw1vE1LSMgwR+6c4V8oARvwA1gP7N5wewlb4Xb+CNtpPnpD9Nj3Our6rrsxKyZfgZ/ygYwrbBh4PoyW2rLefW/6g14uiCk7lk3gPhTLfrcpwoYNZSafX+BoJQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

    public static async Task CheckAndUpdateAsync(Func<string, Task<bool>> confirmUpdate, Func<Func<Task>, Task>? runWithProgress = null)
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TermServMultiScreen-Updater/1.0");

            // 1. Interroger GitHub pour la derniere release
            var releaseUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            var release = await client.GetFromJsonAsync<GitHubRelease>(releaseUrl);
            if (release == null || string.IsNullOrEmpty(release.TagName)) return;

            string tag = release.TagName.TrimStart('v', 'V');
            if (!Version.TryParse(tag, out var remoteVersion)) return;

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (currentVersion == null || remoteVersion <= currentVersion) return; // Deja a jour

            // 2. Demander confirmation à l'utilisateur
            if (confirmUpdate != null)
            {
                bool proceed = await confirmUpdate(tag);
                if (!proceed) return;
            }

            // 3. Trouver les assets (.exe et .sig)
            var exeAsset = release.Assets?.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            var sigAsset = release.Assets?.FirstOrDefault(a => a.Name.EndsWith(".sig", StringComparison.OrdinalIgnoreCase));

            if (exeAsset == null || sigAsset == null) return; // Assets manquants

            Func<Task> downloadAndApply = async () =>
            {
                // 4. Telecharger les fichiers dans %TEMP%
                string tempFolder = Path.Combine(Path.GetTempPath(), "TermServMultiScreenUpdate");
                Directory.CreateDirectory(tempFolder);

                string exePath = Path.Combine(tempFolder, exeAsset.Name);
                string sigPath = Path.Combine(tempFolder, sigAsset.Name);

                await DownloadFileAsync(client, exeAsset.BrowserDownloadUrl, exePath);
                await DownloadFileAsync(client, sigAsset.BrowserDownloadUrl, sigPath);

                // 5. Verifier la signature RSA
                if (!VerifySignature(exePath, sigPath))
                {
                    Log.Write("Erreur de mise à jour : La signature RSA de l'exécutable téléchargé est invalide ou corrompue.");
                    return; // Fichier non authentique, on annule
                }

                // 6. Appliquer la mise à jour
                ApplyUpdate(exePath);
            };

            if (runWithProgress != null)
            {
                await runWithProgress(downloadAndApply);
            }
            else
            {
                await downloadAndApply();
            }
        }
        catch (Exception ex)
        {
            Log.Write($"Erreur lors de la vérification des mises à jour : {ex}");
        }
    }

    private static async Task DownloadFileAsync(HttpClient client, string url, string destinationPath)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        await using var fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await response.Content.CopyToAsync(fs);
    }

    private static bool VerifySignature(string exePath, string sigPath)
    {
        try
        {
            byte[] exeBytes = File.ReadAllBytes(exePath);
            string base64Sig = File.ReadAllText(sigPath).Trim();
            byte[] signatureBytes = Convert.FromBase64String(base64Sig);

            using var rsa = RSA.Create();
            rsa.FromXmlString(PublicKeyXml);

            return rsa.VerifyData(exeBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex)
        {
            Log.Write($"Erreur de vérification de signature : {ex.Message}");
            return false;
        }
    }

    private static void ApplyUpdate(string newExePath)
    {
        string currentExe = Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location;
        string currentFolder = Path.GetDirectoryName(currentExe) ?? string.Empty;
        
        string batPath = Path.Combine(Path.GetTempPath(), "apply_update_termserv.bat");
        string batContent = $@"@echo off
title Mise a jour de TermServMultiScreen...
echo Mise a jour en cours, veuillez patienter...
timeout /t 3 /nobreak > nul
:retry
move /y ""{newExePath}"" ""{currentExe}""
if errorlevel 1 (
    timeout /t 1 /nobreak > nul
    goto retry
)
start """" ""{currentExe}""
del ""%~f0""
";
        File.WriteAllText(batPath, batContent);

        // Lancer le script et se fermer
        Process.Start(new ProcessStartInfo
        {
            FileName = batPath,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        Environment.Exit(0);
    }

    private class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("assets")]
        public GitHubAsset[]? Assets { get; set; }
    }

    private class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
    }
}
