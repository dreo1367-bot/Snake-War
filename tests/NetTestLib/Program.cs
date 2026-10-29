using P2PChat;
using System.Net;

var a = new NetworkManager { Nickname = "Алиса" };
var b = new NetworkManager { Nickname = "Боб" };
b.UseRandomPortsForTesting();

var gotA = new List<string>(); var gotB = new List<string>();
a.OnChatMessage += (n, t) => { lock (gotA) gotA.Add(n + ": " + t); };
b.OnChatMessage += (n, t) => { lock (gotB) gotB.Add(n + ": " + t); };

int connected = 0;
a.OnPeerConnected += (n, ep) => Interlocked.Increment(ref connected);
b.OnPeerConnected += (n, ep) => Interlocked.Increment(ref connected);

a.Start();
await Task.Delay(200);
b.Start();
await Task.Delay(500);

bool ok = a.ConnectTo(new IPEndPoint(IPAddress.Loopback, b.TcpPort));
Console.WriteLine("Connect: " + ok);
await Task.Delay(700);

a.Broadcast("Привет, Боб!");
b.Broadcast("Привет, Алиса! У меня кириллица и цифры 123 ок");
await Task.Delay(700);

Console.WriteLine("A received: " + string.Join(" | ", gotA));
Console.WriteLine("B received: " + string.Join(" | ", gotB));
Console.WriteLine("Peers connected: " + connected);
bool pass = gotB.Contains("Алиса: Привет, Боб!") && gotA.Contains("Боб: Привет, Алиса! У меня кириллица и цифры 123 ок") && connected >= 1;
Console.WriteLine(pass ? "TEST PASSED" : "TEST FAILED");
a.Dispose(); b.Dispose();
Environment.Exit(pass ? 0 : 1);
