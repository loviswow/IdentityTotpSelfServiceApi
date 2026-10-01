using System.Buffers.Binary; using System.Security.Cryptography;
namespace IdentityTotpSelfServiceApi.Tests;
public static class TotpHelper
{
 public static string Generate(string base32Key,DateTimeOffset? now=null){var key=Base32(base32Key);long step=(now??DateTimeOffset.UtcNow).ToUnixTimeSeconds()/30;Span<byte> c=stackalloc byte[8];BinaryPrimitives.WriteInt64BigEndian(c,step);using var h=new HMACSHA1(key);var hash=h.ComputeHash(c.ToArray());int o=hash[^1]&0xf;int bin=((hash[o]&0x7f)<<24)|(hash[o+1]<<16)|(hash[o+2]<<8)|hash[o+3];return (bin%1000000).ToString("D6");}
 static byte[] Base32(string s){const string a="ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";s=s.TrimEnd('=').ToUpperInvariant();var bits=0;var val=0;var b=new List<byte>();foreach(var ch in s){var i=a.IndexOf(ch);if(i<0)continue;val=(val<<5)|i;bits+=5;if(bits>=8){b.Add((byte)((val>>(bits-8))&255));bits-=8;}}return b.ToArray();}
}
