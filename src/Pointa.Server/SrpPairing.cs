using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Agreement.Srp;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;

namespace Pointa.Server;

internal sealed class SrpPairing
{
    private readonly Srp6Server server = new();
    private byte[]? key;
    private readonly string pairingId;
    public string Salt { get; }
    public string B { get; }

    public SrpPairing(string serverId, string pairingId, string certificatePin, string code)
    {
        this.pairingId = pairingId;
        var salt = RandomNumberGenerator.GetBytes(16);
        Salt = Convert.ToHexString(salt).ToLowerInvariant();
        var verifier = new Srp6VerifierGenerator();
        verifier.Init(Srp6StandardGroups.rfc5054_3072, new Sha256Digest());
        var identity = Encoding.UTF8.GetBytes($"pointa-srp-v1\n{serverId}\n{pairingId}\n{certificatePin.ToLowerInvariant()}");
        var password = Encoding.ASCII.GetBytes(code);
        var v = verifier.GenerateVerifier(salt, identity, password);
        CryptographicOperations.ZeroMemory(password);
        server.Init(Srp6StandardGroups.rfc5054_3072, v, new Sha256Digest(), new SecureRandom());
        B = Encode(server.GenerateServerCredentials(), 384);
    }

    public string Prove(string a, string m1)
    {
        var publicA = Decode(a, 384);
        if (publicA.SignValue <= 0 || publicA.CompareTo(Srp6StandardGroups.rfc5054_3072.N) >= 0)
            throw new ProtocolException("invalid_proof");
        server.CalculateSecret(publicA);
        if (!server.VerifyClientEvidenceMessage(Decode(m1, 32))) throw new ProtocolException("invalid_proof");
        var m2 = Encode(server.CalculateServerEvidenceMessage(), 32);
        key = Convert.FromHexString(Encode(server.CalculateSessionKey(), 32));
        return m2;
    }

    public bool Confirm(string confirmation)
    {
        if (key is null || confirmation.Length != 64) return false;
        try
        {
            var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("pointa-srp-confirm-v1\n" + pairingId));
            return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(confirmation));
        }
        catch (FormatException) { return false; }
        finally { CryptographicOperations.ZeroMemory(key); key = null; }
    }

    private static BigInteger Decode(string value, int bytes)
    {
        if (value.Length != bytes * 2 || !value.All(Uri.IsHexDigit)) throw new ProtocolException("invalid_proof");
        return new BigInteger(1, Convert.FromHexString(value));
    }
    private static string Encode(BigInteger value, int bytes) => value.ToString(16).PadLeft(bytes * 2, '0');
}

internal sealed class ProtocolException(string code) : Exception(code);
