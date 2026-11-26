using System;
using System.IO;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using SkiaSharp;

namespace NzbDrone.Core.MediaCover
{
    public interface IImageResizer
    {
        void Resize(string source, string destination, int height);
    }

    public class ImageResizer : IImageResizer
    {
        private const int JpegQuality = 92;

        private readonly IDiskProvider _diskProvider;
        private readonly bool _enabled;

        public ImageResizer(IDiskProvider diskProvider, IPlatformInfo platformInfo)
        {
            _diskProvider = diskProvider;

            _enabled = true;
        }

        public void Resize(string source, string destination, int height)
        {
            if (!_enabled)
            {
                return;
            }

            try
            {
                using var original = SKBitmap.Decode(source);

                if (original == null)
                {
                    throw new InvalidOperationException($"Unable to decode image '{source}'");
                }

                var width = (int)Math.Round((double)original.Width * height / original.Height);
                var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

                using var resized = original.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell));

                if (resized == null)
                {
                    throw new InvalidOperationException($"Unable to resize image '{source}'");
                }

                using var image = SKImage.FromBitmap(resized);
                using var data = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);

                using var output = File.Create(destination);
                data.SaveTo(output);
            }
            catch
            {
                if (_diskProvider.FileExists(destination))
                {
                    _diskProvider.DeleteFile(destination);
                }

                throw;
            }
        }
    }
}
