using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace P2PChat
{
    /// <summary>Сетевой менеджер: TCP-сервер (чат) + UDP-обнаружение соседей в LAN.</summary>
    public class NetworkManager : IDisposable
    {
        // ---------- Протокол сообщений (текстовый, \n в конце) ----------
        // HELLO <ник>            — представление при подключении
        // MSG <ник>:<текст>      — сообщение чата
        // PING / PONG            — проверка живости
        // DISCOVER <ник>         — UDP-запрос "кто в сети?"
        // DISCOVER_ACK <ник>     — UDP-ответ "я здесь"

        public const int DefaultTcpPort = 51111;   // порт чата
        public const int DefaultUdpPort = 51112;   // порт обнаружения

        private int _tcpPort = DefaultTcpPort;
        private int _udpPort = DefaultUdpPort;
        public int TcpPort => _tcpPort;
        public int UdpPort => _udpPort;

        public event Action<string, string> OnChatMessage;          // ник, текст
        public event Action<string, IPEndPoint> OnPeerConnected;    // ник, endpoint
        public event Action<string> OnPeerDisconnected;             // ник
        public event Action<string, IPEndPoint> OnPeerDiscovered;   // автонайденный сосед
        public event Action<string> OnLog;                          // служебные строки

        private TcpListener _tcpListener;
        private UdpClient _udp;
        private readonly Dictionary<TcpClient, PeerInfo> _peers = new();
        private readonly HashSet<IPEndPoint> _discoveredAddrs = new();
        private readonly object _lock = new();
        private volatile bool _running;
        private string _nickname = "Гость";

        public string Nickname { get => _nickname; set => _nickname = value; }

        /// <summary>Тесты: занять свободные порты вместо стандартных.</summary>
        public void UseRandomPortsForTesting()
        {
            _tcpPort = FreePort(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _udpPort = FreePort(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        }

        private static int FreePort(AddressFamily fam, SocketType type, ProtocolType proto)
        {
            var tmp = new Socket(fam, type, proto);
            tmp.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            int p = ((IPEndPoint)tmp.LocalEndPoint).Port;
            tmp.Close();
            return p;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;

            // --- TCP сервер: принимаем входящие подключения ---
            _tcpListener = new TcpListener(IPAddress.Any, _tcpPort);
            _tcpListener.Start();
            _tcpListener.BeginAcceptTcpClient(AcceptCallback, null);
            Log("TCP-сервер запущен на порту " + _tcpPort);

            // --- UDP сокет: рассылка и приём обнаружения ---
            try
            {
                _udp = new UdpClient(_udpPort);
                try { _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1); } catch { }
                _udp.BeginReceive(UdpCallback, null);
            }
            catch (Exception ex)
            {
                // порт занят другим экземпляром на этой машине — discovery отключается, чат работает
                _udp = null;
                Log("UDP-обнаружение недоступно: " + ex.Message);
            }

            // --- периодическая анонс-рассылка в подсеть ---
            new Thread(AnnounceLoop) { IsBackground = true }.Start();
        }

        public void Dispose()
        {
            _running = false;
            try { _tcpListener?.Stop(); } catch { }
            try { _udp?.Close(); } catch { }
            lock (_lock)
            {
                foreach (var c in _peers.Keys)
                    try { c.Close(); } catch { }
                _peers.Clear();
            }
        }

        // ==================== TCP (ЧАТ) ====================
        private void AcceptCallback(IAsyncResult ar)
        {
            if (!_running) return;
            TcpClient client = null;
            try { client = _tcpListener.EndAcceptTcpClient(ar); } catch { }

            if (client != null)
            {
                var peer = new PeerInfo { Client = client, Nick = "?" };
                lock (_lock) _peers[client] = peer;
                try { client.GetStream().BeginRead(peer.Buffer, 0, peer.Buffer.Length, ReadCallback, peer); }
                catch { DropPeer(peer); }
            }
            try { _tcpListener.BeginAcceptTcpClient(AcceptCallback, null); } catch { }
        }

        private void ReadCallback(IAsyncResult ar)
        {
            var peer = (PeerInfo)ar.AsyncState;
            int len = 0;
            try { len = peer.Client.GetStream().EndRead(ar); } catch { }

            if (len > 0)
            {
                peer.Sb.Append(Encoding.UTF8.GetString(peer.Buffer, 0, len));
                string data = peer.Sb.ToString();
                int idx;
                while ((idx = data.IndexOf('\n')) >= 0)
                {
                    string line = data.Substring(0, idx).Trim('\r');
                    data = data.Substring(idx + 1);
                    HandleLine(peer, line);
                }
                peer.Sb.Clear();
                peer.Sb.Append(data);

                try { peer.Client.GetStream().BeginRead(peer.Buffer, 0, peer.Buffer.Length, ReadCallback, peer); }
                catch { DropPeer(peer); }
            }
            else DropPeer(peer);
        }

        private void HandleLine(PeerInfo peer, string line)
        {
            if (line.StartsWith("HELLO "))
            {
                peer.Nick = line.Substring(6).Trim();
                OnPeerConnected?.Invoke(peer.Nick, peer.Client.Client.RemoteEndPoint as IPEndPoint);
                Log("Подключился: " + peer.Nick);
                SendTo(peer, "HELLO " + _nickname); // представить в ответ
            }
            else if (line.StartsWith("MSG "))
            {
                string rest = line.Substring(4);
                int colon = rest.IndexOf(':');
                if (colon > 0)
                    OnChatMessage?.Invoke(rest.Substring(0, colon), rest.Substring(colon + 1));
            }
            else if (line == "PING") SendTo(peer, "PONG");
        }

        private void DropPeer(PeerInfo peer)
        {
            lock (_lock)
            {
                if (!_peers.ContainsKey(peer.Client)) return;
                _peers.Remove(peer.Client);
            }
            try { peer.Client.Close(); } catch { }
            OnPeerDisconnected?.Invoke(peer.Nick);
            Log("Отключился: " + peer.Nick);
        }

        /// <summary>Подключиться к соседу по IP.</summary>
        public bool ConnectTo(IPEndPoint ep)
        {
            try
            {
                var client = new TcpClient();
                client.Connect(ep); // синхронно: для LAN это мгновенно
                var peer = new PeerInfo { Client = client, Nick = ep.Address.ToString() };
                lock (_lock) _peers[client] = peer;
                SendTo(peer, "HELLO " + _nickname);
                client.GetStream().BeginRead(peer.Buffer, 0, peer.Buffer.Length, ReadCallback, peer);
                Log("Вы подключились к " + ep);
                return true;
            }
            catch (Exception ex)
            {
                Log("Не удалось подключиться к " + ep + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>Разослать сообщение всем подключённым соседям.</summary>
        public void Broadcast(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes("MSG " + _nickname + ":" + Sanitize(text) + "\n");
            List<PeerInfo> dead = null;
            lock (_lock)
            {
                foreach (var kv in _peers)
                {
                    try { kv.Key.GetStream().Write(bytes, 0, bytes.Length); }
                    catch { (dead ??= new List<PeerInfo>()).Add(kv.Value); }
                }
            }
            if (dead != null) foreach (var p in dead) DropPeer(p);
        }

        private void SendTo(PeerInfo peer, string line)
        {
            try
            {
                byte[] b = Encoding.UTF8.GetBytes(Sanitize(line) + "\n");
                peer.Client.GetStream().Write(b, 0, b.Length);
            }
            catch { }
        }

        private static string Sanitize(string s) => s.Replace("\r", "").Replace("\n", "");

        // ==================== UDP (ОБНАРУЖЕНИЕ) ====================
        private void UdpCallback(IAsyncResult ar)
        {
            if (!_running || _udp == null) return;
            try
            {
                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = _udp.EndReceive(ar, ref remote);
                string msg = Encoding.UTF8.GetString(data).Trim();

                if (msg.StartsWith("DISCOVER "))
                {
                    string theirNick = msg.Substring(9).Trim();
                    ReplyDiscover(remote);
                    RegisterDiscovery(theirNick, remote.Address);
                }
                else if (msg.StartsWith("DISCOVER_ACK "))
                {
                    RegisterDiscovery(msg.Substring(13).Trim(), remote.Address);
                }
            }
            catch { }
            try { _udp.BeginReceive(UdpCallback, null); } catch { }
        }

        private void RegisterDiscovery(string nick, IPAddress ip)
        {
            if (IPAddress.IsLoopback(ip)) return; // не соединяемся сами с собой
            var tcpEp = new IPEndPoint(ip, _tcpPort);
            bool isNew;
            lock (_lock) isNew = _discoveredAddrs.Add(tcpEp);
            if (isNew)
            {
                OnPeerDiscovered?.Invoke(nick, tcpEp);
                Log("Найден в сети: " + nick + " (" + ip + ") — автоподключение...");
                ThreadPool.QueueUserWorkItem(_ => ConnectTo(tcpEp));
            }
        }

        private void ReplyDiscover(IPEndPoint to)
        {
            try
            {
                byte[] b = Encoding.UTF8.GetBytes("DISCOVER_ACK " + Sanitize(_nickname));
                _udp.Send(b, b.Length, to);
            }
            catch { }
        }

        /// <summary>Фон: каждые 3 сек слать DISCOVER на broadcast всех активных IPv4-карт.</summary>
        private void AnnounceLoop()
        {
            while (_running)
            {
                try
                {
                    if (_udp != null)
                    {
                        byte[] b = Encoding.UTF8.GetBytes("DISCOVER " + Sanitize(_nickname));
                        foreach (var brd in GetBroadcastAddresses())
                            try { _udp.Send(b, b.Length, new IPEndPoint(brd, _udpPort)); } catch { }
                    }
                }
                catch { }
                Thread.Sleep(3000);
            }
        }

        /// <summary>Broadcast-адреса всех активных IPv4 интерфейсов (работает за роутером в LAN).</summary>
        public static IEnumerable<IPAddress> GetBroadcastAddresses()
        {
            var set = new HashSet<IPAddress>();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (ua.IPv4Mask == null) continue;
                    var a = ua.Address.GetAddressBytes();
                    var m = ua.IPv4Mask.GetAddressBytes();
                    var br = new byte[4];
                    for (int i = 0; i < 4; i++) br[i] = (byte)(a[i] | (255 - m[i]));
                    set.Add(new IPAddress(br));
                }
            }
            set.Add(IPAddress.Broadcast);
            return set;
        }

        private class PeerInfo
        {
            public TcpClient Client;
            public string Nick;
            public readonly byte[] Buffer = new byte[4096];
            public readonly StringBuilder Sb = new StringBuilder();
        }

        private void Log(string s) => OnLog?.Invoke(s);
    }
}
