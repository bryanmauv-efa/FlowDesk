using FlowDesk.Core.Rdp;
using FlowDesk.Core;
using System.Security.Cryptography.X509Certificates;

await Check(false, true, true, true, 0, 0, "Silent setup reuses trusted certificate");
await Check(false, false, false, false, 1, 0, "Silent fresh setup fails without creating certificate or prompting");
await Check(true, false, true, true, 0, 0, "Interactive setup creates and validates certificate");
await Check(true, true, false, true, 0, 1, "Interactive setup requests publisher approval");
await Check(true, true, false, false, 1, 1, "Refused elevation fails setup");
await Check(false, true, false, true, 1, 0, "Silent setup never requests elevation");
RdpCertificateService.Stored = false;
RdpTrust.Trusted = true;
if (await RdpInstaller.RunAsync(true) != 1) throw new Exception("Missing root trust must fail setup");
Console.WriteLine("All provisioning checks passed without changing certificate stores.");

static async Task Check(bool interactive, bool preconfigured, bool publisherTrusted, bool acceptElevation,
    int expectedExit, int expectedElevation, string label)
{
    RdpCertificateService.Preconfigured = preconfigured;
    RdpCertificateService.Stored = true;
    RdpTrust.Trusted = publisherTrusted;
    RdpTrust.Initializations = 0;
    RdpPublisherTrustService.Accept = acceptElevation;
    RdpPublisherTrustService.Elevations = 0;
    NativeMethods.Messages = 0;
    var exit = await RdpInstaller.RunAsync(interactive);
    if (exit != expectedExit || RdpPublisherTrustService.Elevations != expectedElevation
        || (!interactive && NativeMethods.Messages != 0)
        || (!interactive && !preconfigured && RdpTrust.Initializations != 0)) throw new Exception(label);
    Console.WriteLine("OK: " + label);
}

namespace FlowDesk.Core
{
    public static class Log { public static void Write(string message) { } }
    public static class Paths { public static string LogFile => "test.log"; }
    public static class NativeMethods
    {
        public const int MB_ICONERROR = 16;
        public static int Messages;
        public static int MessageBox(int hwnd, string text, string title, int flags) { Messages++; return 0; }
    }
}
namespace FlowDesk.Core.Rdp
{
    public class RdpCertificateInfo { public string Sha1 => "test"; }
    public class RdpTrustStatus
    {
        public bool CanSign => true;
        public RdpCertificateInfo Certificate => new();
        public bool PublisherTrusted => RdpTrust.Trusted;
    }
    public static class RdpCertificateService
    {
        public static bool Preconfigured, Stored;
        public static bool HasTrustedCertificate() => Preconfigured;
        public static bool IsInStore(StoreName name, string sha1) => Stored;
    }
    public static class RdpTrust
    {
        public static bool Trusted;
        public static int Initializations;
        public static Task<RdpTrustStatus> InitializeAsync() { Initializations++; return Task.FromResult(new RdpTrustStatus()); }
        public static Task<RdpTrustStatus> RefreshAsync() => Task.FromResult(new RdpTrustStatus());
    }
    public static class RdpPublisherTrustService
    {
        public enum ConfigureOutcome { NeedsElevation }
        public static bool Accept;
        public static int Elevations;
        public static (ConfigureOutcome Outcome, string Detail) TryConfigure(RdpCertificateInfo cert) => (ConfigureOutcome.NeedsElevation, "Permission required");
        public static Task<(bool Completed, string Detail)> RunProvisioningElevatedAsync(RdpCertificateInfo cert)
        {
            Elevations++;
            RdpTrust.Trusted = Accept;
            return Task.FromResult((Accept, "Elevation result"));
        }
    }
}
