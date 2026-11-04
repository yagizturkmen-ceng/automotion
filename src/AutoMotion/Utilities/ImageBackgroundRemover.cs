using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AutoMotion.Utilities
{
    /// <summary>
    /// Kullanıcının kendi araç görseli için basit beyaz-arkaplan temizleme ve sıkı kırpma.
    /// "Araç Listesi" entegrasyonunda Python/Pillow ile yapılan işlemin (hysteresis flood-fill)
    /// aynısının çalışma zamanında (runtime) C#/WPF ile birebir uygulanmasıdır.
    /// </summary>
    public static class ImageBackgroundRemover
    {
        public static WriteableBitmap Process(BitmapSource source)
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = converted.PixelWidth;
            int height = converted.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);

            bool[] strongBg = new bool[width * height];
            bool[] weakBg = new bool[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * stride + x * 4;
                    byte b = pixels[i];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];
                    int min = Math.Min(r, Math.Min(g, b));
                    int idx = y * width + x;
                    strongBg[idx] = min > 210;
                    weakBg[idx] = min > 165;
                }
            }

            // Kenardan başlayan hysteresis flood-fill: sadece kuvvetli-beyaz sınır piksellerinden
            // başlayıp zayıf-beyaz (anti-alias) bölgeye doğru genişler.
            bool[] filled = new bool[width * height];
            var queue = new Queue<(int x, int y)>();

            void SeedBorder(int x, int y)
            {
                int idx = y * width + x;
                if (strongBg[idx] && !filled[idx])
                {
                    filled[idx] = true;
                    queue.Enqueue((x, y));
                }
            }

            for (int x = 0; x < width; x++)
            {
                SeedBorder(x, 0);
                SeedBorder(x, height - 1);
            }
            for (int y = 0; y < height; y++)
            {
                SeedBorder(0, y);
                SeedBorder(width - 1, y);
            }

            int[] dxs = { 1, -1, 0, 0 };
            int[] dys = { 0, 0, 1, -1 };

            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + dxs[k], ny = cy + dys[k];
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                    int nIdx = ny * width + nx;
                    if (filled[nIdx] || !weakBg[nIdx]) continue;
                    filled[nIdx] = true;
                    queue.Enqueue((nx, ny));
                }
            }

            int minX = width, minY = height, maxX = -1, maxY = -1;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    int i = y * stride + x * 4;
                    if (filled[idx])
                    {
                        pixels[i + 3] = 0; // alpha
                    }
                    else
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (maxX < minX || maxY < minY)
            {
                // Tamamen boş/beyaz görsel - kırpmadan olduğu gibi dön
                var fallback = new WriteableBitmap(converted);
                return fallback;
            }

            const int pad = 3;
            minX = Math.Max(0, minX - pad);
            minY = Math.Max(0, minY - pad);
            maxX = Math.Min(width - 1, maxX + pad);
            maxY = Math.Min(height - 1, maxY + pad);

            int cropW = maxX - minX + 1;
            int cropH = maxY - minY + 1;
            var result = new WriteableBitmap(cropW, cropH, converted.DpiX, converted.DpiY, PixelFormats.Bgra32, null);

            byte[] cropped = new byte[cropW * 4 * cropH];
            for (int y = 0; y < cropH; y++)
            {
                Array.Copy(pixels, (minY + y) * stride + minX * 4, cropped, y * cropW * 4, cropW * 4);
            }
            result.WritePixels(new Int32Rect(0, 0, cropW, cropH), cropped, cropW * 4, 0);

            return result;
        }
    }
}
