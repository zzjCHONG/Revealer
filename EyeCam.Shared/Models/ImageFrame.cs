using System;
using EyeCam.Shared.Native;

namespace EyeCam.Shared.Models
{
    /// <summary>图像帧数据</summary>
    public class ImageFrame : IDisposable
    {
        public int Width { get; }
        public int Height { get; }
        public int Stride { get; }
        public int PixelFormat { get; }
        public int DataSize { get; }
        public ulong BlockId { get; }
        public ulong TimeStamp { get; }
        public byte[] Data { get; }

        private bool _disposed = false;

        /// <summary>从Native数据构造图像帧</summary>
        internal ImageFrame(NativeMethods.ImageData imageData)
        {
            Width = imageData.width;
            Height = imageData.height;
            Stride = imageData.stride;
            PixelFormat = imageData.pixelFormat;
            DataSize = imageData.dataSize;
            BlockId = imageData.blockId;
            TimeStamp = imageData.timeStamp;

            // 复制图像数据到托管内存
            if (imageData.pData != IntPtr.Zero && imageData.dataSize > 0)
            {
                Data = new byte[imageData.dataSize];
                System.Runtime.InteropServices.Marshal.Copy(
                    imageData.pData,
                    Data,
                    0,
                    imageData.dataSize);
            }
            else
            {
                Data = new byte[0];
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // 托管内存会自动回收
                _disposed = true;
            }
        }

        public override string ToString()
        {
            return $"{Width}x{Height}, 格式={PixelFormat}, 大小={DataSize}, ID={BlockId}";
        }
    }
}