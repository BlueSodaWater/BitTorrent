using BaseLibS.Parse.Endian;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace BitTorrent
{
    public class DataRequest
    {

    }

    public class DataPackage
    {

    }

    public enum MessageType : int
    {
        Unknown = -3,
        Handshake = -2,
        KeepAlive = -1,
        Choke = 0,
        Unchoke = 1,
        Interested = 2,
        NotInterested = 3,
        Have = 4,
        Bitfield = 5,
        Request = 6,
        Piece = 7,
        Cancel = 8,
        Port = 9,
    }

    public class Peer
    {
        public event EventHandler Disconnected;
        public event EventHandler StateChanged;
        public event EventHandler<DataRequest> BlockRequested;
        public event EventHandler<DataRequest> BlockCancelled;
        public event EventHandler<DataPackage> BlockReceived;

        public string LocalId { get; set; }
        public string Id { get; set; }

        public Torrent Torrent { get; private set; }

        public IPEndPoint IPEndPoint { get; private set; }
        public string Key { get { return IPEndPoint.ToString(); } }

        private TcpClient TcpClient { get; set; }
        private NetworkStream stream { get; set; }
        private const int bufferSize = 256;
        private byte[] streamBuffer = new byte[bufferSize];
        private List<byte> data = new List<byte>();

        public bool[] IsPieceDownloaded = new bool[0];
        public string PiecesDownloaded { get { return String.Join("", IsPieceDownloaded.Select(x => Convert.ToInt32(x))); } }
        public int PiecesRequiredAvailable { get { return IsPieceDownloaded.Select((x, i) => x && !Torrent.IsPieceVerified[i]).Count(x => x); } }
        public int PiecesDownloadedCount { get { return IsPieceDownloaded.Count(x => x); } }
        public bool IsCompleted { get { return PiecesDownloadedCount == Torrent.PieceCount; } }

        public bool IsDisconnected;

        public bool IsHandshakeSent;
        public bool IsPositionSent;
        public bool IsChokeSent = true;
        public bool IsInterestedSent = false;

        public bool IsHandshakeReceived;
        public bool IsChokeReceived = true;
        public bool IsInterestedReceived = false;

        public bool[][] IsBlockRequested = new bool[0][];
        public int BlocksRequested { get { return IsBlockRequested.Sum(x => x.Count(y => y)); } }

        public DateTime LastActive;
        public DateTime LastKeepAlive = DateTime.MinValue;

        public long Uploaded;
        public long Downloaded;

        public Peer(Torrent torrent, string localId, TcpClient client) : this(torrent, localId)
        {
            this.TcpClient = client;
            this.IPEndPoint = (IPEndPoint)client.Client.RemoteEndPoint;
        }

        public Peer(Torrent torrent, string localId, IPEndPoint endPoint) : this(torrent, localId)
        {
            this.IPEndPoint = endPoint;
        }

        public Peer(Torrent torrent, string localId)
        {
            this.LocalId = localId;
            this.Torrent = torrent;

            this.LastActive = DateTime.UtcNow;
            this.IsPieceDownloaded = new bool[Torrent.PieceCount];
            this.IsBlockRequested = new bool[Torrent.PieceCount][];
            for (var i = 0; i < Torrent.PieceCount; i++)
                IsBlockRequested[i] = new bool[Torrent.GetBlockCount(i)];
        }

        public void Connect()
        {
            if (TcpClient == null)
            {
                TcpClient = new TcpClient();
                try
                {
                    TcpClient.Connect(IPEndPoint);
                }
                catch (Exception e)
                {
                    Disconnect();
                    return;
                }
            }

            stream = TcpClient.GetStream();
            stream.BeginRead(streamBuffer, 0, Peer.bufferSize, new AsyncCallback(HandleRead), null);

            SendHandshake();
            if (IsHandshakeReceived)
                SentBitfield(Torrent.IsPieceVerified);
        }

        private void Disconnect()
        {
            if (!IsDisconnected)
            {
                IsDisconnected = true;
            }

            if (TcpClient != null)
                TcpClient.Close();

            if (Disconnected != null)
                Disconnected(this, new EventArgs());
        }

        private void SentBytes(byte[] bytes)
        {
            try
            {
                stream.Write(bytes, 0, bytes.Length);
            }
            catch (Exception e)
            {
                Disconnect();
            }
        }

        private void HandleRead(IAsyncResult ar)
        {
            int bytes = 0;
            try
            {
                bytes = stream.EndRead(ar);
            }
            catch (Exception e)
            {
                Disconnect();
                return;
            }

            data.AddRange(streamBuffer.Take(bytes));

            int messageLength = GetMessageLength(data);
            while (data.Count >= messageLength)
            {
                HandleMessage(data.Take(messageLength).ToArray());
                data = data.Skip(messageLength).ToList();

                messageLength = GetMessageLength(data);
            }

            try
            {
                stream.BeginRead(streamBuffer, 0, Peer.bufferSize, new AsyncCallback(HandleRead), null);
            }
            catch (Exception e)
            {
                Disconnect();
            }
        }

        private void HandleMessage(byte[] vs)
        {
            throw new NotImplementedException();
        }

        private int GetMessageLength(List<byte> data)
        {
            if (!IsHandshakeReceived)
                return 68;

            if (data.Count < 4)
                return int.MaxValue;

            return EndianBitConverter.Big.ToInt32(data.ToArray(), 0) + 4;
        }

        private void SendHandshake()
        {
            throw new NotImplementedException();
        }

        private void SentBitfield(bool[] isPieceVerified)
        {

        }

        public static bool DecodeHandshake(byte[] bytes, out byte[] hash, out string id)
        {
            hash = new byte[20];
            id = "";

            if (bytes.Length != 68 || bytes[0] != 19)
            {
                return false;
            }

            if (Encoding.UTF8.GetString(bytes.Skip(1).Take(19).ToArray()) != "BitTorrent protocol")
            {
                return false;
            }

            hash = bytes.Skip(28).Take(20).ToArray();

            id = Encoding.UTF8.GetString(bytes.Skip(48).Take(20).ToArray());

            return true;
        }

        public static byte[] EncodeHandshake(byte[] hash, string id)
        {
            byte[] message = new byte[68];
            message[0] = 19;
            Buffer.BlockCopy(Encoding.UTF8.GetBytes("BitTorrent protocol"), 0, message, 1, 19);
            Buffer.BlockCopy(hash, 0, message, 28, 20);
            Buffer.BlockCopy(Encoding.UTF8.GetBytes(id), 0, message, 48, 20);

            return message;
        }

        public static bool DecodeKeepAlive(byte[] bytes)
        {
            if (bytes.Length != 4 || EndianBitConverter.Big.ToInt32(bytes, 0) != 0)
            {
                return false;
            }
            return true;
        }

        public static byte[] EncodeKeepAlive()
        {
            return EndianBitConverter.Big.GetBytes(0);
        }

        public static bool DecodeChoke(byte[] bytes)
        {
            return DecodeState(bytes, MessageType.Choke);
        }

        public static bool DecodeUnchoke(byte[] bytes)
        {
            return DecodeState(bytes, MessageType.Unchoke);
        }

        public static bool DecodeInterested(byte[] bytes)
        {
            return DecodeState(bytes, MessageType.Interested);
        }

        public static bool DecodeNotInterested(byte[] bytes)
        {
            return DecodeState(bytes, MessageType.NotInterested);
        }

        public static bool DecodeState(byte[] bytes, MessageType type)
        {
            if (bytes.Length != 5 || EndianBitConverter.Big.ToInt32(bytes, 0) != 1 || bytes[4] != (byte)type)
            {
                return false;
            }
            return true;
        }

        public static byte[] EncodeChoke()
        {
            return EncodeState(MessageType.Choke);
        }

        public static byte[] EncodeUnchoke()
        {
            return EncodeState(MessageType.Unchoke);
        }

        public static byte[] EncodeInterested()
        {
            return EncodeState(MessageType.Interested);
        }

        public static byte[] EncodeNotInterested()
        {
            return EncodeState(MessageType.NotInterested);
        }

        public static byte[] EncodeState(MessageType type)
        {
            byte[] message = new byte[5];
            Buffer.BlockCopy(EndianBitConverter.Big.GetBytes(1), 0, message, 0, 4);
            message[4] = (byte)type;
            return message;
        }

        public static bool DecodeHave(byte[] bytes, out int index)
        {
            index = -1;

            if (bytes.Length != 9 || EndianBitConverter.Big.ToInt32(bytes, 0) != 5)
                return false;

            index = EndianBitConverter.Big.ToInt32(bytes, 5);

            return true;
        }

        public static bool DecodeBitfield(byte[] bytes, int pieces, out bool[] isPieceDownloaded)
        {
            isPieceDownloaded = new bool[pieces];

            int expectedLength = Convert.ToInt32(Math.Ceiling(pieces / 8.0)) + 1;

            if (bytes.Length != expectedLength + 4 || EndianBitConverter.Big.ToInt32(bytes, 0) != expectedLength)
                return false;

            BitArray bitfield = new BitArray(bytes.Skip(5).ToArray());

            for (int i = 0; i < pieces; i++)
                isPieceDownloaded[i] = bitfield[bitfield.Length - 1 - i];

            return true;
        }

        public static byte[] EncodeHave(int index)
        {
            byte[] message = new byte[9];
            Buffer.BlockCopy(EndianBitConverter.Big.GetBytes(5), 0, message, 0, 4);
            message[4] = (byte)MessageType.Have;
            Buffer.BlockCopy(EndianBitConverter.Big.GetBytes(index), 0, message, 5, 4);

            return message;
        }

        public static byte[] EncodeBitfield(bool[] isPieceDownloaded)
        {
            int numPieces = isPieceDownloaded.Length;
            int numBytes = Convert.ToInt32(Math.Ceiling(numPieces / 8.0));
            int numBits = numBytes * 8;

            int length = numBytes + 1;

            byte[] message = new byte[length + 4];
            Buffer.BlockCopy(EndianBitConverter.Big.GetBytes(length), 0, message, 0, 4);
            message[4] = (byte)MessageType.Bitfield;

            bool[] downloaded = new bool[numBits];
            for (int i = 0; i < numPieces; i++)
                downloaded[i] = isPieceDownloaded[i];

            BitArray bitfield = new BitArray(downloaded);
            BitArray reversed = new BitArray(numBits);
            for (int i = 0; i < numBits; i++)
                reversed[i] = bitfield[numBits - i - 1];

            reversed.CopyTo(message, 5);

            return message;
        }
    }
}
