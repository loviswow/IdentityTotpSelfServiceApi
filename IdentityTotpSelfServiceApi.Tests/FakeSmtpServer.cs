using System.Collections.Concurrent; using System.Net; using System.Net.Sockets; using System.Text;
namespace IdentityTotpSelfServiceApi.Tests;
// 테스트용 최소 SMTP 서버(127.0.0.1, 평문). EHLO/AUTH PLAIN/MAIL/RCPT/DATA/QUIT만 처리하고 받은 메일을 기록한다.
// FailNext만큼은 MAIL FROM에 451로 응답해 일시 장애를 흉내 낸다.
public sealed class FakeSmtpServer:IAsyncDisposable
{
 public sealed record Message(string From,string[] To,string Data,string? AuthUser,string? AuthPassword);
 readonly TcpListener listener=new(IPAddress.Loopback,0); readonly CancellationTokenSource cts=new(); readonly Task loop;
 public ConcurrentQueue<Message> Messages {get;}=new(); public int Port=>((IPEndPoint)listener.LocalEndpoint).Port; public int FailNext; public int MailAttempts;
 // RejectRecipient에 해당하는 수신자는 RCPT에 550(영구 오류)으로 거부한다. RcptAttempts는 수신자별 RCPT 시도 횟수다.
 public Func<string,bool>? RejectRecipient; public ConcurrentDictionary<string,int> RcptAttempts {get;}=new();
 public FakeSmtpServer(){listener.Start();loop=Task.Run(AcceptLoop);}
 async Task AcceptLoop(){while(!cts.IsCancellationRequested){TcpClient c;try{c=await listener.AcceptTcpClientAsync(cts.Token);}catch{return;}_=Task.Run(()=>Handle(c));}}
 async Task Handle(TcpClient client)
 {
  using var _=client; var s=client.GetStream(); var r=new StreamReader(s,Encoding.ASCII); var w=new StreamWriter(s,new UTF8Encoding(false)){NewLine="\r\n",AutoFlush=true};
  string? from=null,user=null,pass=null; var to=new List<string>();
  await w.WriteLineAsync("220 fake ESMTP");
  while(await r.ReadLineAsync() is {} line)
  {
   var cmd=line.Length>=4?line[..4].ToUpperInvariant():line.ToUpperInvariant();
   if(cmd=="EHLO"){await w.WriteLineAsync("250-fake");await w.WriteLineAsync("250-8BITMIME");await w.WriteLineAsync("250 AUTH PLAIN");}
   else if(cmd=="HELO")await w.WriteLineAsync("250 fake");
   else if(cmd=="AUTH"){var p=Encoding.UTF8.GetString(Convert.FromBase64String(line.Split(' ')[2])).Split('\0');user=p[1];pass=p[2];await w.WriteLineAsync("235 ok");}
   else if(cmd=="MAIL"){Interlocked.Increment(ref MailAttempts);if(FailNext>0){FailNext--;await w.WriteLineAsync("451 try later");continue;}from=Addr(line);to.Clear();await w.WriteLineAsync("250 ok");}
   else if(cmd=="RCPT"){var a=Addr(line);RcptAttempts.AddOrUpdate(a,1,(_,n)=>n+1);if(RejectRecipient?.Invoke(a)==true){await w.WriteLineAsync("550 5.1.1 no such user");continue;}to.Add(a);await w.WriteLineAsync("250 ok");}
   else if(cmd=="DATA"){await w.WriteLineAsync("354 go");var sb=new StringBuilder();while(await r.ReadLineAsync() is {} d&&d!=".")sb.AppendLine(d.StartsWith("..")?d[1..]:d);Messages.Enqueue(new(from!,to.ToArray(),sb.ToString(),user,pass));await w.WriteLineAsync("250 queued");}
   else if(cmd=="RSET"){from=null;to.Clear();await w.WriteLineAsync("250 ok");}
   else if(cmd=="QUIT"){await w.WriteLineAsync("221 bye");return;}
   else await w.WriteLineAsync("250 ok");
  }
 }
 static string Addr(string line){var i=line.IndexOf('<');var j=line.IndexOf('>');return i>=0&&j>i?line[(i+1)..j]:line;}
 public async Task<Message> WaitFor(Func<Message,bool> match,int timeoutMs=10000){var until=DateTime.UtcNow.AddMilliseconds(timeoutMs);while(DateTime.UtcNow<until){var m=Messages.FirstOrDefault(match);if(m is not null)return m;await Task.Delay(50);}throw new TimeoutException("SMTP 메일이 도착하지 않았습니다.");}
 public async ValueTask DisposeAsync(){cts.Cancel();listener.Stop();try{await loop;}catch{}}
}
