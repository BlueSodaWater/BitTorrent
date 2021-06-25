using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace BitTorrent
{
    public class FileItem
    {
        public string Path;
        public long Size;
        public long Offset;

        public string FormattedSize;
    }

    public class Torrent
    {
        #region Events

        public event EventHandler<List<IPEndPoint>> PeerListUpdated;

        #endregion

        #region Properties

        public string Name { get; private set; }
        public bool? IsPrivate { get; private set; }
        public List<FileItem> Files { get; private set; } = new List<FileItem>();
        public string FileDirectory { get { return (Files.Count > 1 ? Name + Path.DirectorySeparatorChar : ""); } }
        public string DownloadDirectory { get; private set; }

        public List<Tracker> Trackers { get; } = new List<Tracker>();
        public string Comment { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreationDate { get; set; }
        public Encoding Encoding { get; set; }

        public int BlockSize { get; private set; }
        public int PieceSize { get; private set; }
        public long TotalSize { get { return Files.Sum(x => x.Size); } }

        public string FormattedPieceSize { get { return ByteToString(PieceSize); } }
        public string FormattedTotalSize { get { return ByteToString(TotalSize); } }

        public int PieceCount { get { return PieceHashes.Length; } }

        public byte[][] PieceHashes { get; private set; }
        public bool[] IsPieceVerified { get; private set; }
        public bool[][] IsBlockAcquired { get; private set; }

        public string VerifiedPiecesString { get { return String.Join("", IsPieceVerified.Select(x => x ? 1 : 0)); } }
        public int VerifiedPieceCount { get { return IsPieceVerified.Count(x => x); } }
        public double VerifiedRatio { get { return VerifiedPieceCount / (double)PieceCount; } }
        public bool IsCompleted { get { return VerifiedPieceCount == PieceCount; } }
        public bool IsStarted { get { return VerifiedPieceCount > 0; } }

        public long Uploaded { get; set; } = 0;
        public long Downloaded { get { return PieceSize * VerifiedPieceCount; } }
        public long Left { get { return TotalSize - Downloaded; } }

        public byte[] Infohash { get; private set; } = new byte[20];
        public string HexStringInfohash { get { return string.Join("", this.Infohash.Select(x => x.ToString("x2"))); } }
        public string UrlSafeStringInfohash { get { return Encoding.UTF8.GetString(WebUtility.UrlEncodeToBytes(this.Infohash, 0, 20)); } }

        #endregion

        #region Fields

        private object[] fileWriteLocks;
        private static SHA1 sha1 = SHA1.Create();

        #endregion

        public Torrent(string name, string location, List<FileItem> files, List<string> trackers, int pieceSize, byte[] pieceHashes = null, int blockSize = 16384, bool? isPrivate = false)
        {
            this.Name = name;
            this.DownloadDirectory = location;
            this.Files = files;
            this.fileWriteLocks = new object[files.Count];
            for (var i = 0; i < this.Files.Count; i++)
                fileWriteLocks[i] = new object();

            if (trackers != null)
            {
                foreach (string url in trackers)
                {
                    Tracker tracker = new Tracker(url);
                    this.Trackers.Add(tracker);
                    tracker.PeerListUpdated += HandlePeerListUpdated;
                }
            }

            this.PieceSize = pieceSize;
            this.BlockSize = blockSize;
            this.IsPrivate = isPrivate;

            int count = Convert.ToInt32(Math.Ceiling(TotalSize / Convert.ToDouble(pieceSize)));

            this.PieceHashes = new byte[count][];
            this.IsPieceVerified = new bool[count];
            this.IsBlockAcquired = new bool[count][];

            for (var i = 0; i < PieceCount; i++)
                IsBlockAcquired[i] = new bool[GetBlockCount(i)];

            if (pieceHashes == null)
            {
                for (int i = 0; i < PieceCount; i++)
                    this.PieceHashes[i] = GetHash(i);
            }
            else
            {
                for (int i = 0; i < PieceCount; i++)
                {
                    this.PieceHashes[i] = new byte[20];
                    Buffer.BlockCopy(pieceHashes, i * 20, this.PieceHashes[i], 0, 20);
                }
            }

            object info = TorrentInfoToBEncodingObject(this);
            byte[] bytes = BEncoding.Encode(info);
            Infohash = SHA1.Create().ComputeHash(bytes);

            for (var i = 0; i < PieceCount; i++)
                this.Verify(i);
        }

        private void Verify(int i)
        {
            throw new NotImplementedException();
        }

        private object TorrentInfoToBEncodingObject(Torrent torrent)
        {
            throw new NotImplementedException();
        }

        private byte[] GetHash(int i)
        {
            throw new NotImplementedException();
        }

        private void HandlePeerListUpdated(object sender, List<IPEndPoint> e)
        {
            throw new NotImplementedException();
        }

        public byte[] Read(long start,int length)
        {
            
        } 

        public int GetPieceSize(int piece)
        {
            if (piece == PieceCount - 1)
            {
                int remainder = Convert.ToInt32(TotalSize % PieceSize);
                if (remainder != 0)
                    return remainder;
            }

            return PieceSize;
        }

        public int GetBlockSize(int piece, int block)
        {
            if (block == GetBlockCount(piece - 1))
            {
                int remainder = Convert.ToInt32(TotalSize % BlockSize);
                if (remainder != 0)
                    return remainder;
            }

            return BlockSize;
        }

        public int GetBlockCount(int piece)
        {
            return Convert.ToInt32(Math.Ceiling(GetPieceSize(piece) / (double)BlockSize));
        }

        public static string ByteToString(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
            if (bytes == 0)
                return "0" + units[0];
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return num + units[place];
        }
    }
}
