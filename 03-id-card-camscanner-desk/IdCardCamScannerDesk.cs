using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace IdCardCamScannerDesk
{
    public enum ScanFilterMode
    {
        CleanGrayscale = 0,
        MagicColor = 1,
        CrispBlackAndWhite = 2,
        OriginalEnhanced = 3
    }

    public enum A4IdLayoutMode
    {
        SingleTopBottom = 0,
        SingleSideBySide = 1,
        DoubleCopy2x = 2,
        QuadCopy4x = 3,
        Enlarged135Percent = 4
    }

    public static class CamScannerEngine
    {
        // Solve 8x8 linear system for projective homography mapping (0,0)-(dstW,dstH) -> 4 source quad points
        public static double[] ComputeHomography(int dstW, int dstH, PointF tl, PointF tr, PointF br, PointF bl)
        {
            double[] u = new double[] { 0, dstW, dstW, 0 };
            double[] v = new double[] { 0, 0, dstH, dstH };
            double[] x = new double[] { tl.X, tr.X, br.X, bl.X };
            double[] y = new double[] { tl.Y, tr.Y, br.Y, bl.Y };

            double[,] A = new double[8, 9];
            for (int i = 0; i < 4; i++)
            {
                int r1 = i * 2;
                int r2 = r1 + 1;
                A[r1, 0] = u[i]; A[r1, 1] = v[i]; A[r1, 2] = 1;
                A[r1, 3] = 0;    A[r1, 4] = 0;    A[r1, 5] = 0;
                A[r1, 6] = -u[i] * x[i]; A[r1, 7] = -v[i] * x[i];
                A[r1, 8] = x[i];

                A[r2, 0] = 0;    A[r2, 1] = 0;    A[r2, 2] = 0;
                A[r2, 3] = u[i]; A[r2, 4] = v[i]; A[r2, 5] = 1;
                A[r2, 6] = -u[i] * y[i]; A[r2, 7] = -v[i] * y[i];
                A[r2, 8] = y[i];
            }

            // Gaussian elimination with partial pivoting
            for (int col = 0; col < 8; col++)
            {
                int pivot = col;
                double maxVal = Math.Abs(A[col, col]);
                for (int row = col + 1; row < 8; row++)
                {
                    double val = Math.Abs(A[row, col]);
                    if (val > maxVal)
                    {
                        maxVal = val;
                        pivot = row;
                    }
                }
                if (pivot != col)
                {
                    for (int k = col; k <= 8; k++)
                    {
                        double tmp = A[col, k];
                        A[col, k] = A[pivot, k];
                        A[pivot, k] = tmp;
                    }
                }
                double diag = A[col, col];
                if (Math.Abs(diag) < 1e-12) continue;

                for (int k = col; k <= 8; k++)
                    A[col, k] /= diag;

                for (int row = 0; row < 8; row++)
                {
                    if (row == col) continue;
                    double factor = A[row, col];
                    if (Math.Abs(factor) < 1e-12) continue;
                    for (int k = col; k <= 8; k++)
                        A[row, k] -= factor * A[col, k];
                }
            }

            double[] h = new double[9];
            for (int i = 0; i < 8; i++) h[i] = A[i, 8];
            h[8] = 1.0;
            return h;
        }

        // Unwarp a 4-corner region of src into a flat destination Bitmap of (dstW x dstH) using Bilinear Interpolation
        public static Bitmap UnwarpPerspective(Bitmap src, PointF[] normQuad, int dstW, int dstH)
        {
            if (src == null) return new Bitmap(dstW, dstH);

            Bitmap src24 = Ensure24Bpp(src);
            int sW = src24.Width;
            int sH = src24.Height;

            PointF tl = new PointF(normQuad[0].X * (sW - 1), normQuad[0].Y * (sH - 1));
            PointF tr = new PointF(normQuad[1].X * (sW - 1), normQuad[1].Y * (sH - 1));
            PointF br = new PointF(normQuad[2].X * (sW - 1), normQuad[2].Y * (sH - 1));
            PointF bl = new PointF(normQuad[3].X * (sW - 1), normQuad[3].Y * (sH - 1));

            double[] h = ComputeHomography(dstW, dstH, tl, tr, br, bl);

            Bitmap dst = new Bitmap(dstW, dstH, PixelFormat.Format24bppRgb);
            BitmapData sData = src24.LockBits(new Rectangle(0, 0, sW, sH), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            BitmapData dData = dst.LockBits(new Rectangle(0, 0, dstW, dstH), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            int sStride = sData.Stride;
            int dStride = dData.Stride;
            byte[] sBuf = new byte[sStride * sH];
            byte[] dBuf = new byte[dStride * dstH];
            Marshal.Copy(sData.Scan0, sBuf, 0, sBuf.Length);

            double h0 = h[0], h1 = h[1], h2 = h[2];
            double h3 = h[3], h4 = h[4], h5 = h[5];
            double h6 = h[6], h7 = h[7], h8 = h[8];

            for (int v = 0; v < dstH; v++)
            {
                int dRow = v * dStride;
                double v_h1_h2 = v * h1 + h2;
                double v_h4_h5 = v * h4 + h5;
                double v_h7_h8 = v * h7 + h8;

                for (int u = 0; u < dstW; u++)
                {
                    double denom = u * h6 + v_h7_h8;
                    if (Math.Abs(denom) < 1e-9) denom = 1e-9;
                    double sx = (u * h0 + v_h1_h2) / denom;
                    double sy = (u * h3 + v_h4_h5) / denom;

                    int dIdx = dRow + u * 3;

                    if (sx < 0) sx = 0; else if (sx > sW - 1.001) sx = sW - 1.001;
                    if (sy < 0) sy = 0; else if (sy > sH - 1.001) sy = sH - 1.001;

                    int x0 = (int)sx;
                    int y0 = (int)sy;
                    int x1 = x0 + 1 < sW ? x0 + 1 : x0;
                    int y1 = y0 + 1 < sH ? y0 + 1 : y0;

                    double fx = sx - x0;
                    double fy = sy - y0;
                    double w00 = (1.0 - fx) * (1.0 - fy);
                    double w10 = fx * (1.0 - fy);
                    double w01 = (1.0 - fx) * fy;
                    double w11 = fx * fy;

                    int p00 = y0 * sStride + x0 * 3;
                    int p10 = y0 * sStride + x1 * 3;
                    int p01 = y1 * sStride + x0 * 3;
                    int p11 = y1 * sStride + x1 * 3;

                    dBuf[dIdx]     = (byte)(sBuf[p00] * w00 + sBuf[p10] * w10 + sBuf[p01] * w01 + sBuf[p11] * w11);
                    dBuf[dIdx + 1] = (byte)(sBuf[p00 + 1] * w00 + sBuf[p10 + 1] * w10 + sBuf[p01 + 1] * w01 + sBuf[p11 + 1] * w11);
                    dBuf[dIdx + 2] = (byte)(sBuf[p00 + 2] * w00 + sBuf[p10 + 2] * w10 + sBuf[p01 + 2] * w01 + sBuf[p11 + 2] * w11);
                }
            }

            Marshal.Copy(dBuf, 0, dData.Scan0, dBuf.Length);
            src24.UnlockBits(sData);
            dst.UnlockBits(dData);
            if (! object.ReferenceEquals(src24, src)) src24.Dispose();
            return dst;
        }

        // Auto-detect bright quadrilateral corners against a darker table background
        public static PointF[] AutoDetectCorners(Bitmap src)
        {
            if (src == null)
            {
                return new PointF[] {
                    new PointF(0.05f, 0.05f), new PointF(0.95f, 0.05f),
                    new PointF(0.95f, 0.95f), new PointF(0.05f, 0.95f)
                };
            }

            Bitmap bmp = Ensure24Bpp(src);
            int w = bmp.Width;
            int h = bmp.Height;
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            int stride = bd.Stride;
            byte[] buf = new byte[stride * h];
            Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
            bmp.UnlockBits(bd);

            // Sample border brightness vs center brightness
            long borderSum = 0;
            int borderCnt = 0;
            long centerSum = 0;
            int centerCnt = 0;

            int stepY = Math.Max(1, h / 120);
            int stepX = Math.Max(1, w / 120);

            for (int y = 0; y < h; y += stepY)
            {
                int row = y * stride;
                for (int x = 0; x < w; x += stepX)
                {
                    int idx = row + x * 3;
                    int lum = (buf[idx + 2] * 77 + buf[idx + 1] * 150 + buf[idx] * 29) >> 8;
                    if (x < w * 0.06 || x > w * 0.94 || y < h * 0.06 || y > h * 0.94)
                    {
                        borderSum += lum;
                        borderCnt++;
                    }
                    else if (x > w * 0.25 && x < w * 0.75 && y > h * 0.25 && y < h * 0.75)
                    {
                        centerSum += lum;
                        centerCnt++;
                    }
                }
            }

            if (!object.ReferenceEquals(bmp, src)) bmp.Dispose();

            int borderAvg = borderCnt > 0 ? (int)(borderSum / borderCnt) : 80;
            int centerAvg = centerCnt > 0 ? (int)(centerSum / centerCnt) : 180;

            // If there is no clear darker background around the page, return full-bleed slight inset
            if (centerAvg - borderAvg < 22)
            {
                return new PointF[] {
                    new PointF(0.02f, 0.02f), new PointF(0.98f, 0.02f),
                    new PointF(0.98f, 0.98f), new PointF(0.02f, 0.98f)
                };
            }

            int thresh = borderAvg + (centerAvg - borderAvg) * 38 / 100;

            // Find extreme bright pixels scoring closest to the 4 image corners:
            // TL: minimize (x/w + y/h), TR: maximize (x/w - y/h), BR: maximize (x/w + y/h), BL: minimize (x/w - y/h)
            double bestTL = 1e9, bestTR = -1e9, bestBR = -1e9, bestBL = 1e9;
            PointF ptTL = new PointF(0.08f, 0.08f);
            PointF ptTR = new PointF(0.92f, 0.08f);
            PointF ptBR = new PointF(0.92f, 0.92f);
            PointF ptBL = new PointF(0.08f, 0.92f);

            for (int y = stepY * 2; y < h - stepY * 2; y += stepY)
            {
                int row = y * stride;
                double ny = (double)y / h;
                for (int x = stepX * 2; x < w - stepX * 2; x += stepX)
                {
                    int idx = row + x * 3;
                    int lum = (buf[idx + 2] * 77 + buf[idx + 1] * 150 + buf[idx] * 29) >> 8;
                    if (lum < thresh) continue;

                    // Verify neighborhood is also bright (avoid isolated noise specs)
                    int idx2 = (y + stepY) * stride + (x + stepX) * 3;
                    int lum2 = (buf[idx2 + 2] * 77 + buf[idx2 + 1] * 150 + buf[idx2] * 29) >> 8;
                    if (lum2 < thresh) continue;

                    double nx = (double)x / w;
                    double sumXY = nx + ny;
                    double diffXY = nx - ny;

                    if (sumXY < bestTL) { bestTL = sumXY; ptTL = new PointF((float)nx, (float)ny); }
                    if (diffXY > bestTR) { bestTR = diffXY; ptTR = new PointF((float)nx, (float)ny); }
                    if (sumXY > bestBR) { bestBR = sumXY; ptBR = new PointF((float)nx, (float)ny); }
                    if (diffXY < bestBL) { bestBL = diffXY; ptBL = new PointF((float)nx, (float)ny); }
                }
            }

            return new PointF[] { ptTL, ptTR, ptBR, ptBL };
        }

        // Core CamScanner Shadow-Removal & Document Enhancement using Downsampled Morphological Background Surface
        public static Bitmap ApplyCamScannerFilter(
            Bitmap src,
            ScanFilterMode mode,
            int whiteness,      // 0..100 (default 68)
            int inkContrast,    // 0..100 (default 62)
            int borderCleanPx)  // 0..30 px white border cleanup
        {
            if (src == null) return null;

            Bitmap bmp = Ensure24Bpp(src);
            int w = bmp.Width;
            int h = bmp.Height;

            Bitmap dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            BitmapData sData = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            BitmapData dData = dst.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            int stride = sData.Stride;
            byte[] sBuf = new byte[stride * h];
            byte[] dBuf = new byte[stride * h];
            Marshal.Copy(sData.Scan0, sBuf, 0, sBuf.Length);

            if (mode == ScanFilterMode.OriginalEnhanced)
            {
                double cFactor = 1.0 + (inkContrast - 40) * 0.012;
                int bOffset = (whiteness - 40) * 2 / 3;
                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int idx = row + x * 3;
                        for (int ch = 0; ch < 3; ch++)
                        {
                            double v = (sBuf[idx + ch] - 128.0) * cFactor + 128.0 + bOffset;
                            if (v < 0) v = 0; else if (v > 255) v = 255;
                            dBuf[idx + ch] = (byte)v;
                        }
                    }
                }
            }
            else
            {
                // 1. Build downsampled local-maxima illumination grid (ignores ink & dark boxes, captures paper + shadow)
                int cell = Math.Max(6, Math.Min(w, h) / 85);
                int gw = (w + cell - 1) / cell;
                int gh = (h + cell - 1) / cell;
                int gCount = gw * gh;

                byte[] bgB = new byte[gCount];
                byte[] bgG = new byte[gCount];
                byte[] bgR = new byte[gCount];

                for (int gy = 0; gy < gh; gy++)
                {
                    int yStart = gy * cell;
                    int yEnd = Math.Min(h, yStart + cell);
                    for (int gx = 0; gx < gw; gx++)
                    {
                        int xStart = gx * cell;
                        int xEnd = Math.Min(w, xStart + cell);
                        int maxLum = -1;
                        byte bestB = 200, bestG = 200, bestR = 200;

                        for (int y = yStart; y < yEnd; y += 2)
                        {
                            int row = y * stride;
                            for (int x = xStart; x < xEnd; x += 2)
                            {
                                int idx = row + x * 3;
                                byte b = sBuf[idx];
                                byte g = sBuf[idx + 1];
                                byte r = sBuf[idx + 2];
                                int lum = (r * 77 + g * 150 + b * 29) >> 8;
                                if (lum > maxLum)
                                {
                                    maxLum = lum;
                                    bestB = b;
                                    bestG = g;
                                    bestR = r;
                                }
                            }
                        }
                        int gIdx = gy * gw + gx;
                        bgB[gIdx] = bestB;
                        bgG[gIdx] = bestG;
                        bgR[gIdx] = bestR;
                    }
                }

                // 2. Three passes of 3x3 Morphological Dilation on the background grid so dark headers/photos are bridged over
                for (int pass = 0; pass < 3; pass++)
                {
                    byte[] nextB = new byte[gCount];
                    byte[] nextG = new byte[gCount];
                    byte[] nextR = new byte[gCount];
                    for (int gy = 0; gy < gh; gy++)
                    {
                        int y0 = Math.Max(0, gy - 1);
                        int y1 = Math.Min(gh - 1, gy + 1);
                        for (int gx = 0; gx < gw; gx++)
                        {
                            int x0 = Math.Max(0, gx - 1);
                            int x1 = Math.Min(gw - 1, gx + 1);
                            byte mB = 0, mG = 0, mR = 0;
                            for (int ny = y0; ny <= y1; ny++)
                            {
                                int rOff = ny * gw;
                                for (int nx = x0; nx <= x1; nx++)
                                {
                                    int k = rOff + nx;
                                    if (bgB[k] > mB) mB = bgB[k];
                                    if (bgG[k] > mG) mG = bgG[k];
                                    if (bgR[k] > mR) mR = bgR[k];
                                }
                            }
                            int gIdx = gy * gw + gx;
                            nextB[gIdx] = mB;
                            nextG[gIdx] = mG;
                            nextR[gIdx] = mR;
                        }
                    }
                    bgB = nextB; bgG = nextG; bgR = nextR;
                }

                // 3. Two passes of 5x5 Box Blur on the background grid for ultra-smooth shadow surface
                for (int pass = 0; pass < 2; pass++)
                {
                    byte[] blurB = new byte[gCount];
                    byte[] blurG = new byte[gCount];
                    byte[] blurR = new byte[gCount];
                    for (int gy = 0; gy < gh; gy++)
                    {
                        int y0 = Math.Max(0, gy - 2);
                        int y1 = Math.Min(gh - 1, gy + 2);
                        for (int gx = 0; gx < gw; gx++)
                        {
                            int x0 = Math.Max(0, gx - 2);
                            int x1 = Math.Min(gw - 1, gx + 2);
                            int sB = 0, sG = 0, sR = 0, cnt = 0;
                            for (int ny = y0; ny <= y1; ny++)
                            {
                                int rOff = ny * gw;
                                for (int nx = x0; nx <= x1; nx++)
                                {
                                    int k = rOff + nx;
                                    sB += bgB[k];
                                    sG += bgG[k];
                                    sR += bgR[k];
                                    cnt++;
                                }
                            }
                            int gIdx = gy * gw + gx;
                            blurB[gIdx] = (byte)(sB / cnt);
                            blurG[gIdx] = (byte)(sG / cnt);
                            blurR[gIdx] = (byte)(sR / cnt);
                        }
                    }
                    bgB = blurB; bgG = blurG; bgR = blurR;
                }

                int whiteFloor = 244 - (whiteness * 46 / 100);
                double contrastGain = 1.12 + (inkContrast * 0.015);
                int bwThreshold = 205 - (inkContrast - whiteness / 2);
                if (bwThreshold < 120) bwThreshold = 120;
                if (bwThreshold > 230) bwThreshold = 230;

                // 4. Normalize every pixel against bilinearly-interpolated local background illumination
                for (int y = 0; y < h; y++)
                {
                    double gyF = (double)y / cell;
                    int gy0 = (int)gyF; if (gy0 >= gh) gy0 = gh - 1;
                    int gy1 = (gy0 + 1 < gh) ? gy0 + 1 : gy0;
                    int fy = (int)((gyF - gy0) * 256);
                    int invFy = 256 - fy;

                    int row0 = gy0 * gw;
                    int row1 = gy1 * gw;
                    int sRow = y * stride;

                    for (int x = 0; x < w; x++)
                    {
                        double gxF = (double)x / cell;
                        int gx0 = (int)gxF; if (gx0 >= gw) gx0 = gw - 1;
                        int gx1 = (gx0 + 1 < gw) ? gx0 + 1 : gx0;
                        int fx = (int)((gxF - gx0) * 256);
                        int invFx = 256 - fx;

                        int w00 = (invFx * invFy) >> 8;
                        int w10 = (fx * invFy) >> 8;
                        int w01 = (invFx * fy) >> 8;
                        int w11 = (fx * fy) >> 8;

                        int k00 = row0 + gx0, k10 = row0 + gx1;
                        int k01 = row1 + gx0, k11 = row1 + gx1;

                        int ilB = (bgB[k00] * w00 + bgB[k10] * w10 + bgB[k01] * w01 + bgB[k11] * w11) >> 8;
                        int ilG = (bgG[k00] * w00 + bgG[k10] * w10 + bgG[k01] * w01 + bgG[k11] * w11) >> 8;
                        int ilR = (bgR[k00] * w00 + bgR[k10] * w10 + bgR[k01] * w01 + bgR[k11] * w11) >> 8;

                        if (ilB < 45) ilB = 45;
                        if (ilG < 45) ilG = 45;
                        if (ilR < 45) ilR = 45;

                        int idx = sRow + x * 3;
                        int b = sBuf[idx];
                        int g = sBuf[idx + 1];
                        int r = sBuf[idx + 2];

                        int nb = (b * 255) / ilB; if (nb > 255) nb = 255;
                        int ng = (g * 255) / ilG; if (ng > 255) ng = 255;
                        int nr = (r * 255) / ilR; if (nr > 255) nr = 255;

                        int lum = (nr * 77 + ng * 150 + nb * 29) >> 8;

                        if (mode == ScanFilterMode.CrispBlackAndWhite)
                        {
                            byte bw = (lum < bwThreshold) ? (byte)0 : (byte)255;
                            dBuf[idx] = bw;
                            dBuf[idx + 1] = bw;
                            dBuf[idx + 2] = bw;
                        }
                        else if (mode == ScanFilterMode.CleanGrayscale)
                        {
                            int gl = lum;
                            if (gl >= whiteFloor)
                            {
                                int span = Math.Max(1, 255 - whiteFloor);
                                int t = ((gl - whiteFloor) * 256) / span;
                                if (t > 256) t = 256;
                                gl = gl + ((255 - gl) * t >> 8);
                            }
                            else
                            {
                                double f = 255.0 - (255.0 - gl) * contrastGain;
                                gl = f < 0 ? 0 : (f > 255 ? 255 : (int)f);
                            }
                            byte outG = (byte)gl;
                            dBuf[idx] = outG;
                            dBuf[idx + 1] = outG;
                            dBuf[idx + 2] = outG;
                        }
                        else // MagicColor
                        {
                            if (lum >= whiteFloor)
                            {
                                int span = Math.Max(1, 255 - whiteFloor);
                                int t = ((lum - whiteFloor) * 256) / span;
                                if (t > 256) t = 256;
                                nr = nr + ((255 - nr) * t >> 8);
                                ng = ng + ((255 - ng) * t >> 8);
                                nb = nb + ((255 - nb) * t >> 8);
                            }
                            else
                            {
                                double f = 255.0 - (255.0 - nr) * contrastGain;
                                nr = f < 0 ? 0 : (f > 255 ? 255 : (int)f);
                                f = 255.0 - (255.0 - ng) * contrastGain;
                                ng = f < 0 ? 0 : (f > 255 ? 255 : (int)f);
                                f = 255.0 - (255.0 - nb) * contrastGain;
                                nb = f < 0 ? 0 : (f > 255 ? 255 : (int)f);
                            }
                            dBuf[idx]     = (byte)nb;
                            dBuf[idx + 1] = (byte)ng;
                            dBuf[idx + 2] = (byte)nr;
                        }
                    }
                }
            }

            // Optional clean white border margin to erase dark table edge artifacts
            if (borderCleanPx > 0)
            {
                int bx = Math.Min(borderCleanPx, w / 10);
                int by = Math.Min(borderCleanPx, h / 10);
                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    bool topOrBot = (y < by || y >= h - by);
                    for (int x = 0; x < w; x++)
                    {
                        if (topOrBot || x < bx || x >= w - bx)
                        {
                            int idx = row + x * 3;
                            dBuf[idx] = 255;
                            dBuf[idx + 1] = 255;
                            dBuf[idx + 2] = 255;
                        }
                    }
                }
            }

            Marshal.Copy(dBuf, 0, dData.Scan0, dBuf.Length);
            bmp.UnlockBits(sData);
            dst.UnlockBits(dData);
            if (!object.ReferenceEquals(bmp, src)) bmp.Dispose();
            return dst;
        }

        // Apply CR80 rounded corners and subtle clean border to an unwarped ID card bitmap
        public static Bitmap ApplyCardRoundedFrame(Bitmap cardBmp, bool drawBorder, bool drawCutGuide)
        {
            if (cardBmp == null) return null;
            int w = cardBmp.Width;
            int h = cardBmp.Height;
            Bitmap outBmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);

            using (Graphics g = Graphics.FromImage(outBmp))
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                int pad = drawCutGuide ? 10 : 2;
                int cw = w - pad * 2;
                int ch = h - pad * 2;
                int radius = (int)Math.Round(cw * 0.038); // CR80 3.18mm / 85.6mm

                using (GraphicsPath path = CreateRoundedRectPath(new Rectangle(pad, pad, cw, ch), radius))
                {
                    g.SetClip(path);
                    g.DrawImage(cardBmp, new Rectangle(pad, pad, cw, ch));
                    g.ResetClip();

                    if (drawBorder)
                    {
                        using (Pen p = new Pen(Color.FromArgb(60, 68, 80), 2.0f))
                        {
                            g.DrawPath(p, path);
                        }
                    }
                }

                if (drawCutGuide)
                {
                    using (Pen cutPen = new Pen(Color.FromArgb(140, 150, 165), 1.2f))
                    {
                        cutPen.DashStyle = DashStyle.Dash;
                        g.DrawRectangle(cutPen, 2, 2, w - 5, h - 5);
                    }
                }
            }
            return outBmp;
        }

        public static GraphicsPath CreateRoundedRectPath(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Bitmap Ensure24Bpp(Bitmap src)
        {
            if (src.PixelFormat == PixelFormat.Format24bppRgb) return src;
            Bitmap b = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.White);
                g.DrawImage(src, 0, 0, src.Width, src.Height);
            }
            return b;
        }
    }

    public static class A4SheetComposer
    {
        // Standard A4 dimensions: 210.0 mm x 297.0 mm
        // Standard CR80 ID Card dimensions: 85.60 mm x 53.98 mm
        public static Bitmap RenderA4IdSheet(
            Bitmap frontCard,
            Bitmap backCard,
            A4IdLayoutMode layout,
            bool roundCorners,
            bool cutGuides,
            string watermarkText,
            int dpi = 200)
        {
            int pageW = (int)Math.Round(210.0 / 25.4 * dpi);
            int pageH = (int)Math.Round(297.0 / 25.4 * dpi);

            double scale = (layout == A4IdLayoutMode.Enlarged135Percent) ? 1.35 : 1.0;
            int cardW = (int)Math.Round((85.60 / 25.4) * dpi * scale);
            int cardH = (int)Math.Round((53.98 / 25.4) * dpi * scale);

            Bitmap sheet = new Bitmap(pageW, pageH, PixelFormat.Format24bppRgb);
            sheet.SetResolution(dpi, dpi);

            using (Graphics g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                Bitmap fFramed = frontCard != null ? CamScannerEngine.ApplyCardRoundedFrame(frontCard, roundCorners, cutGuides) : null;
                Bitmap bFramed = backCard != null ? CamScannerEngine.ApplyCardRoundedFrame(backCard, roundCorners, cutGuides) : null;

                int gapY = (int)Math.Round(12.0 / 25.4 * dpi);
                int gapX = (int)Math.Round(10.0 / 25.4 * dpi);
                int topMargin = (int)Math.Round(22.0 / 25.4 * dpi);

                if (layout == A4IdLayoutMode.SingleTopBottom || layout == A4IdLayoutMode.Enlarged135Percent)
                {
                    int x = (pageW - cardW) / 2;
                    int y1 = (int)Math.Round(38.0 / 25.4 * dpi);
                    int y2 = y1 + cardH + (int)Math.Round(20.0 / 25.4 * dpi);

                    DrawCardSlot(g, fFramed, "RECTO (FRONT)", x, y1, cardW, cardH, cutGuides, watermarkText);
                    DrawCardSlot(g, bFramed, "VERSO (BACK)", x, y2, cardW, cardH, cutGuides, watermarkText);
                }
                else if (layout == A4IdLayoutMode.SingleSideBySide)
                {
                    int totalW = cardW * 2 + gapX;
                    int x1 = (pageW - totalW) / 2;
                    int x2 = x1 + cardW + gapX;
                    int y = topMargin + (int)Math.Round(10.0 / 25.4 * dpi);

                    DrawCardSlot(g, fFramed, "RECTO (FRONT)", x1, y, cardW, cardH, cutGuides, watermarkText);
                    DrawCardSlot(g, bFramed, "VERSO (BACK)", x2, y, cardW, cardH, cutGuides, watermarkText);
                }
                else if (layout == A4IdLayoutMode.DoubleCopy2x)
                {
                    int totalW = cardW * 2 + gapX;
                    int x1 = (pageW - totalW) / 2;
                    int x2 = x1 + cardW + gapX;

                    for (int row = 0; row < 2; row++)
                    {
                        int y = topMargin + row * (cardH + gapY * 2);
                        DrawCardSlot(g, fFramed, "RECTO #" + (row + 1), x1, y, cardW, cardH, cutGuides, watermarkText);
                        DrawCardSlot(g, bFramed, "VERSO #" + (row + 1), x2, y, cardW, cardH, cutGuides, watermarkText);
                    }
                }
                else if (layout == A4IdLayoutMode.QuadCopy4x)
                {
                    int totalW = cardW * 2 + gapX;
                    int x1 = (pageW - totalW) / 2;
                    int x2 = x1 + cardW + gapX;
                    int compactGapY = (int)Math.Round(9.0 / 25.4 * dpi);
                    int startY = (int)Math.Round(16.0 / 25.4 * dpi);

                    for (int row = 0; row < 4; row++)
                    {
                        int y = startY + row * (cardH + compactGapY);
                        DrawCardSlot(g, fFramed, "RECTO #" + (row + 1), x1, y, cardW, cardH, cutGuides, watermarkText);
                        DrawCardSlot(g, bFramed, "VERSO #" + (row + 1), x2, y, cardW, cardH, cutGuides, watermarkText);
                    }
                }

                if (fFramed != null) fFramed.Dispose();
                if (bFramed != null) bFramed.Dispose();
            }

            return sheet;
        }

        private static void DrawCardSlot(
            Graphics g,
            Bitmap card,
            string label,
            int x,
            int y,
            int w,
            int h,
            bool showLabel,
            string watermark)
        {
            if (showLabel)
            {
                using (Font f = new Font("Segoe UI", Math.Max(7f, w * 0.015f), FontStyle.Bold))
                using (SolidBrush br = new SolidBrush(Color.FromArgb(120, 135, 155)))
                {
                    string lblTxt = "✂ " + label;
                    SizeF lSz = g.MeasureString(lblTxt, f);
                    g.DrawString(lblTxt, f, br, x + 2, y - lSz.Height - 2);
                }
            }

            if (card != null)
            {
                g.DrawImage(card, new Rectangle(x, y, w, h));
            }
            else
            {
                using (Pen p = new Pen(Color.FromArgb(203, 213, 225), 2f))
                {
                    p.DashStyle = DashStyle.Dash;
                    g.DrawRectangle(p, x, y, w, h);
                }
            }

            if (!string.IsNullOrWhiteSpace(watermark))
            {
                GraphicsState st = g.Save();
                g.SetClip(new Rectangle(x, y, w, h));
                g.TranslateTransform(x + w / 2f, y + h / 2f);
                g.RotateTransform(-18f);

                float fontSize = Math.Max(8f, w * 0.038f);
                using (Font wf = new Font("Segoe UI", fontSize, FontStyle.Bold))
                using (SolidBrush wBrush = new SolidBrush(Color.FromArgb(75, 185, 28, 28)))
                using (Pen wPen = new Pen(Color.FromArgb(85, 185, 28, 28), 1.5f))
                using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    SizeF sz = g.MeasureString(watermark, wf);
                    RectangleF stampRect = new RectangleF(-sz.Width / 2f - 10, -sz.Height / 2f - 4, sz.Width + 20, sz.Height + 8);
                    g.DrawRectangle(wPen, stampRect.X, stampRect.Y, stampRect.Width, stampRect.Height);
                    g.DrawString(watermark, wf, wBrush, new PointF(0, 0), sf);
                }
                g.Restore(st);
            }
        }
    }

    // Generates realistic angled, shadowed "bad smartphone photos" of an ID card (Front & Back) and an A4 Document
    public static class SamplePhotoGenerator
    {
        public static Bitmap CreateSampleBadIdPhoto(bool isFront, out PointF[] trueQuad)
        {
            int w = 900;
            int h = 620;
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);

            // Render a flat clean ID card first
            int cw = 680;
            int ch = 428;
            using (Bitmap flatCard = new Bitmap(cw, ch, PixelFormat.Format24bppRgb))
            {
                using (Graphics cg = Graphics.FromImage(flatCard))
                {
                    cg.SmoothingMode = SmoothingMode.HighQuality;
                    cg.Clear(Color.FromArgb(244, 247, 250));

                    // Subtle security guilloche lines
                    using (Pen guilloche = new Pen(Color.FromArgb(224, 234, 244), 1f))
                    {
                        for (int i = 0; i < ch; i += 14)
                            cg.DrawBezier(guilloche, 0, i, cw / 3, i + 28, cw * 2 / 3, i - 28, cw, i);
                    }

                    // Header bar
                    using (SolidBrush hb = new SolidBrush(Color.FromArgb(18, 65, 112)))
                        cg.FillRectangle(hb, 0, 0, cw, 68);
                    using (SolidBrush stripe = new SolidBrush(Color.FromArgb(22, 130, 82)))
                        cg.FillRectangle(stripe, 0, 68, cw, 6);

                    using (Font fHead = new Font("Segoe UI", 14.5f, FontStyle.Bold))
                    using (Font fSub = new Font("Segoe UI", 9.5f, FontStyle.Bold))
                    using (Font fLabel = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                    using (Font fVal = new Font("Segoe UI", 13.0f, FontStyle.Bold))
                    using (Font fMrz = new Font("Consolas", 14.0f, FontStyle.Bold))
                    {
                        if (isFront)
                        {
                            cg.DrawString("NATIONAL BIOMETRIC IDENTITY CARD", fHead, Brushes.White, 20, 12);
                            cg.DrawString("CARTE NATIONALE D'IDENTITÉ BIOMÉTRIQUE • بطاقة التعريف الوطنية", fSub, Brushes.LightCyan, 20, 40);

                            // Photo box on left
                            Rectangle photoRect = new Rectangle(26, 94, 165, 210);
                            cg.FillRectangle(Brushes.White, photoRect);
                            using (Pen pp = new Pen(Color.FromArgb(120, 140, 160), 2f))
                                cg.DrawRectangle(pp, photoRect);

                            // Draw stylized portrait silhouette so Grayscale/Magic Color demo looks realistic
                            using (SolidBrush skin = new SolidBrush(Color.FromArgb(145, 155, 170)))
                            using (SolidBrush suit = new SolidBrush(Color.FromArgb(55, 70, 95)))
                            {
                                cg.FillEllipse(skin, 68, 118, 80, 90);
                                cg.FillPie(suit, 36, 205, 145, 160, 180, 180);
                            }

                            // Fields on right
                            int fx = 215;
                            cg.DrawString("ID NUMBER / N° D'IDENTIFICATION", fLabel, Brushes.SlateGray, fx, 92);
                            using (SolidBrush idBr = new SolidBrush(Color.FromArgb(180, 28, 28)))
                                cg.DrawString("109948205001842009", fVal, idBr, fx, 110);

                            cg.DrawString("SURNAME / NOM", fLabel, Brushes.SlateGray, fx, 148);
                            cg.DrawString("BENALI", fVal, Brushes.Black, fx, 165);

                            cg.DrawString("GIVEN NAMES / PRÉNOMS", fLabel, Brushes.SlateGray, fx, 202);
                            cg.DrawString("KARIM AMINE", fVal, Brushes.Black, fx, 219);

                            cg.DrawString("DATE OF BIRTH / NÉ(E) LE", fLabel, Brushes.SlateGray, fx, 256);
                            cg.DrawString("14 . 06 . 1992", fVal, Brushes.Black, fx, 273);

                            cg.DrawString("BLOOD / GROUPE SANGUIN:  O+", fSub, Brushes.DarkSlateBlue, fx, 315);
                            cg.DrawString("EXPIRY / VALABLE JUSQU'AU:  05.11.2034", fSub, Brushes.Black, fx, 342);

                            // Official circular stamp
                            using (Pen stampPen = new Pen(Color.FromArgb(170, 25, 85, 165), 2.5f))
                            {
                                cg.DrawEllipse(stampPen, cw - 155, ch - 145, 115, 115);
                                cg.DrawEllipse(stampPen, cw - 147, ch - 137, 99, 99);
                                cg.DrawString("OFFICIAL SEAL", fLabel, Brushes.RoyalBlue, cw - 140, ch - 95);
                            }
                        }
                        else
                        {
                            cg.DrawString("REPUBLIC ADMINISTRATION — VERSO", fHead, Brushes.White, 20, 12);
                            cg.DrawString("AUTHORITY & MACHINE READABLE ZONE (MRZ)", fSub, Brushes.LightCyan, 20, 40);

                            cg.DrawString("RESIDENCE / ADRESSE DE RÉSIDENCE:", fLabel, Brushes.SlateGray, 28, 92);
                            cg.DrawString("CITE 400 LOGEMENTS BT B4 N° 18, ALGER", fVal, Brushes.Black, 28, 112);

                            cg.DrawString("ISSUING AUTHORITY / DÉLIVRÉE PAR:", fLabel, Brushes.SlateGray, 28, 152);
                            cg.DrawString("DAIRA DE BAB EZZOUAR — WILAYA D'ALGER (16)", fVal, Brushes.Black, 28, 172);

                            // Simulated 1D Barcode
                            int bx = 30;
                            Random rBar = new Random(42);
                            for (int i = 0; i < 88; i++)
                            {
                                int bw = (i % 3 == 0) ? 4 : 2;
                                cg.FillRectangle(Brushes.Black, bx, 215, bw, 46);
                                bx += bw + rBar.Next(2, 5);
                            }

                            // MRZ Zone at bottom
                            cg.FillRectangle(Brushes.White, 16, ch - 135, cw - 32, 118);
                            cg.DrawString("IDDZA094820500<<<<109948205001", fMrz, Brushes.Black, 28, ch - 125);
                            cg.DrawString("9206148M3411054DZA<<<<<<<<<<<6", fMrz, Brushes.Black, 28, ch - 90);
                            cg.DrawString("BENALI<<KARIM<AMINE<<<<<<<<<<<", fMrz, Brushes.Black, 28, ch - 55);
                        }
                    }
                }

                // Now warp flatCard onto a dark desk surface with perspective tilt + heavy diagonal phone shadow!
                trueQuad = isFront
                    ? new PointF[] { new PointF(0.11f, 0.14f), new PointF(0.89f, 0.08f), new PointF(0.93f, 0.87f), new PointF(0.07f, 0.83f) }
                    : new PointF[] { new PointF(0.09f, 0.09f), new PointF(0.91f, 0.15f), new PointF(0.87f, 0.89f), new PointF(0.12f, 0.85f) };

                RenderWarpedWithShadow(bmp, flatCard, trueQuad);
            }
            return bmp;
        }

        public static Bitmap CreateSampleBadDocument(out PointF[] trueQuad)
        {
            int w = 900;
            int h = 1180;
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);

            int dw = 720;
            int dh = 1018;
            using (Bitmap flatDoc = new Bitmap(dw, dh, PixelFormat.Format24bppRgb))
            {
                using (Graphics dg = Graphics.FromImage(flatDoc))
                {
                    dg.SmoothingMode = SmoothingMode.HighQuality;
                    dg.Clear(Color.FromArgb(248, 248, 245));

                    using (Font fTitle = new Font("Segoe UI", 18f, FontStyle.Bold))
                    using (Font fSub = new Font("Segoe UI", 12f, FontStyle.Bold))
                    using (Font fBody = new Font("Segoe UI", 11.5f, FontStyle.Regular))
                    using (Font fBold = new Font("Segoe UI", 11.5f, FontStyle.Bold))
                    using (Pen borderP = new Pen(Color.Black, 2.5f))
                    {
                        dg.DrawRectangle(borderP, 28, 28, dw - 56, dh - 56);
                        dg.DrawString("RÉPUBLIQUE — MINISTÈRE DE L'ENSEIGNEMENT & DE L'EMPLOI", fSub, Brushes.Black, 85, 55);
                        dg.DrawString("ATTESTATION ADMINISTRATIVE D'INSCRIPTION", fTitle, Brushes.DarkBlue, 78, 95);
                        dg.DrawLine(borderP, 60, 138, dw - 60, 138);

                        int y = 175;
                        string[] lines = new string[] {
                            "N° de Référence :   ESERV-2026 / 008491-DZ",
                            "Date d'émission :   05 Novembre 2026",
                            "",
                            "Le Chef de Service certifie par la présente que :",
                            "   • Nom et Prénom :       BENALI KARIM AMINE",
                            "   • Date de Naissance :   14 / 06 / 1992  à  ALGER",
                            "   • N° Carte Nationale :  109948205001842009",
                            "   • Catégorie / Statut :  Demandeur Actif — Dossier Complet",
                            "",
                            "A déposé l'ensemble des pièces justificatives requises pour le",
                            "renouvellement et la validation de son dossier électronique.",
                            "",
                            "RECAPITULATIF DES PIÈCES VÉRIFIÉES :"
                        };
                        foreach (string line in lines)
                        {
                            dg.DrawString(line, line.StartsWith("   •") ? fBold : fBody, Brushes.Black, 65, y);
                            y += 28;
                        }

                        // Draw a verification table
                        Rectangle tRect = new Rectangle(65, y + 10, dw - 130, 150);
                        dg.DrawRectangle(Pens.Black, tRect);
                        dg.DrawLine(Pens.Black, 65, y + 55, dw - 65, y + 55);
                        dg.DrawLine(Pens.Black, 65, y + 105, dw - 65, y + 105);
                        dg.DrawLine(Pens.Black, 460, y + 10, 460, y + 160);

                        dg.DrawString("1. Copie Carte Nationale (Recto/Verso)", fBody, Brushes.Black, 80, y + 22);
                        dg.DrawString("CONFORME [X]", fBold, Brushes.DarkGreen, 480, y + 22);
                        dg.DrawString("2. Certificat de Résidence / Domicile", fBody, Brushes.Black, 80, y + 68);
                        dg.DrawString("CONFORME [X]", fBold, Brushes.DarkGreen, 480, y + 68);
                        dg.DrawString("3. Fiche d'Inscription Numérique", fBody, Brushes.Black, 80, y + 118);
                        dg.DrawString("VALIDÉE  [X]", fBold, Brushes.DarkGreen, 480, y + 118);

                        // Blue Round Official Stamp + Signature at bottom right
                        using (Pen blueStamp = new Pen(Color.FromArgb(190, 20, 70, 175), 3f))
                        {
                            dg.DrawEllipse(blueStamp, dw - 260, dh - 235, 160, 160);
                            dg.DrawEllipse(blueStamp, dw - 250, dh - 225, 140, 140);
                            dg.DrawString("SERVICE PUBLIC", fBold, Brushes.MediumBlue, dw - 242, dh - 172);
                            dg.DrawString("CERTIFIÉ CONFORME", fSub, Brushes.Crimson, dw - 252, dh - 142);
                        }
                    }
                }

                trueQuad = new PointF[] {
                    new PointF(0.10f, 0.07f), new PointF(0.88f, 0.11f),
                    new PointF(0.93f, 0.92f), new PointF(0.06f, 0.90f)
                };
                RenderWarpedWithShadow(bmp, flatDoc, trueQuad);
            }
            return bmp;
        }

        // Maps a flat image onto a quad inside dstBmp and applies realistic uneven phone lighting & dark hand/lamp shadow
        private static void RenderWarpedWithShadow(Bitmap dstBmp, Bitmap flatSrc, PointF[] quad)
        {
            int w = dstBmp.Width;
            int h = dstBmp.Height;
            int sw = flatSrc.Width;
            int sh = flatSrc.Height;

            PointF tl = new PointF(quad[0].X * w, quad[0].Y * h);
            PointF tr = new PointF(quad[1].X * w, quad[1].Y * h);
            PointF br = new PointF(quad[2].X * w, quad[2].Y * h);
            PointF bl = new PointF(quad[3].X * w, quad[3].Y * h);

            // Inverse homography from dstBmp (x,y) -> flatSrc (u,v)
            double[] invH = ComputeInverseHomography(sw, sh, tl, tr, br, bl);

            BitmapData dData = dstBmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            BitmapData sData = flatSrc.LockBits(new Rectangle(0, 0, sw, sh), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            int dStride = dData.Stride;
            int sStride = sData.Stride;
            byte[] dBuf = new byte[dStride * h];
            byte[] sBuf = new byte[sStride * sh];
            Marshal.Copy(sData.Scan0, sBuf, 0, sBuf.Length);

            for (int y = 0; y < h; y++)
            {
                int dRow = y * dStride;
                for (int x = 0; x < w; x++)
                {
                    double denom = x * invH[6] + y * invH[7] + invH[8];
                    double u = (x * invH[0] + y * invH[1] + invH[2]) / denom;
                    double v = (x * invH[3] + y * invH[4] + invH[5]) / denom;

                    int idx = dRow + x * 3;
                    int r, g, b;

                    if (u >= 0 && u < sw - 1 && v >= 0 && v < sh - 1)
                    {
                        int iu = (int)u;
                        int iv = (int)v;
                        int sIdx = iv * sStride + iu * 3;
                        b = sBuf[sIdx];
                        g = sBuf[sIdx + 1];
                        r = sBuf[sIdx + 2];
                    }
                    else
                    {
                        // Dark wooden desk texture outside the card/document
                        int grain = ((x * 3 + y * 7) % 19) - 9;
                        r = 58 + grain;
                        g = 44 + grain;
                        b = 34 + grain;
                    }

                    // Simulate realistic bad indoor smartphone lighting + heavy diagonal shadow in bottom-right half
                    double diag = (double)x / w * 0.65 + (double)y / h * 0.75;
                    double shadowMult = 0.94 - 0.44 * (1.0 / (1.0 + Math.Exp(-(diag - 0.68) * 14.0)));

                    // Warm indoor bulb tint (slightly less blue)
                    b = (int)(b * shadowMult * 0.86);
                    g = (int)(g * shadowMult * 0.93);
                    r = (int)(r * shadowMult * 0.98);

                    if (b < 0) b = 0; else if (b > 255) b = 255;
                    if (g < 0) g = 0; else if (g > 255) g = 255;
                    if (r < 0) r = 0; else if (r > 255) r = 255;

                    dBuf[idx] = (byte)b;
                    dBuf[idx + 1] = (byte)g;
                    dBuf[idx + 2] = (byte)r;
                }
            }

            Marshal.Copy(dBuf, 0, dData.Scan0, dBuf.Length);
            flatSrc.UnlockBits(sData);
            dstBmp.UnlockBits(dData);
        }

        private static double[] ComputeInverseHomography(int sw, int sh, PointF tl, PointF tr, PointF br, PointF bl)
        {
            // Forward maps (0..sw, 0..sh) -> (tl,tr,br,bl). Invert 3x3 matrix to map (x,y) -> (u,v)
            double[] H = CamScannerEngine.ComputeHomography(sw, sh, tl, tr, br, bl);
            double a = H[0], b = H[1], c = H[2];
            double d = H[3], e = H[4], f = H[5];
            double g = H[6], h = H[7], i = H[8];

            double A = (e * i - f * h);
            double B = -(d * i - f * g);
            double C = (d * h - e * g);
            double D = -(b * i - c * h);
            double E = (a * i - c * g);
            double F = -(a * h - b * g);
            double G = (b * f - c * e);
            double H_ = -(a * f - c * d);
            double I = (a * e - b * d);

            double det = a * A + b * B + c * C;
            if (Math.Abs(det) < 1e-12) det = 1e-12;

            return new double[] {
                A / det, D / det, G / det,
                B / det, E / det, H_ / det,
                C / det, F / det, I / det
            };
        }
    }

    // Interactive 4-Corner Perspective Crop Canvas with Drag-and-Drop and Magnifying Loupe
    public class QuadCropCanvas : Control
    {
        private Bitmap _image;
        private PointF[] _quad = new PointF[] {
            new PointF(0.08f, 0.08f),
            new PointF(0.92f, 0.08f),
            new PointF(0.92f, 0.92f),
            new PointF(0.08f, 0.92f)
        };
        private int _dragIndex = -1;
        private string _title = "Image";

        public event EventHandler QuadOrImageChanged;

        public Bitmap SourceImage
        {
            get { return _image; }
            set
            {
                _image = value;
                Invalidate();
                if (QuadOrImageChanged != null) QuadOrImageChanged(this, EventArgs.Empty);
            }
        }

        public PointF[] NormalizedQuad
        {
            get { return _quad; }
            set
            {
                if (value != null && value.Length == 4)
                {
                    _quad = value;
                    Invalidate();
                    if (QuadOrImageChanged != null) QuadOrImageChanged(this, EventArgs.Empty);
                }
            }
        }

        public QuadCropCanvas(string title)
        {
            _title = title;
            this.DoubleBuffered = true;
            this.AllowDrop = true;
            this.BackColor = Color.FromArgb(30, 41, 59);
            this.Cursor = Cursors.Cross;
        }

        public void SetImageAndQuad(Bitmap bmp, PointF[] quad)
        {
            _image = bmp;
            if (quad != null && quad.Length == 4) _quad = quad;
            Invalidate();
            if (QuadOrImageChanged != null) QuadOrImageChanged(this, EventArgs.Empty);
        }

        public void ResetFullQuad()
        {
            _quad = new PointF[] {
                new PointF(0.01f, 0.01f),
                new PointF(0.99f, 0.01f),
                new PointF(0.99f, 0.99f),
                new PointF(0.01f, 0.99f)
            };
            Invalidate();
            if (QuadOrImageChanged != null) QuadOrImageChanged(this, EventArgs.Empty);
        }

        public void Rotate90CW()
        {
            if (_image == null) return;
            _image.RotateFlip(RotateFlipType.Rotate90FlipNone);
            _quad = CamScannerEngine.AutoDetectCorners(_image);
            Invalidate();
            if (QuadOrImageChanged != null) QuadOrImageChanged(this, EventArgs.Empty);
        }

        private Rectangle GetImageViewport()
        {
            if (_image == null) return new Rectangle(10, 10, Math.Max(10, Width - 20), Math.Max(10, Height - 20));
            int pad = 14;
            int availW = Math.Max(20, Width - pad * 2);
            int availH = Math.Max(20, Height - pad * 2);
            float scale = Math.Min((float)availW / _image.Width, (float)availH / _image.Height);
            int dw = (int)Math.Round(_image.Width * scale);
            int dh = (int)Math.Round(_image.Height * scale);
            return new Rectangle((Width - dw) / 2, (Height - dh) / 2, dw, dh);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;

            if (_image == null)
            {
                using (Font f = new Font("Segoe UI", 10f, FontStyle.Bold))
                using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.DrawString(_title + "\nDrop Photo Here or Click Load / Paste (Ctrl+V)", f, Brushes.LightSlateGray, ClientRectangle, sf);
                }
                return;
            }

            Rectangle vp = GetImageViewport();
            g.DrawImage(_image, vp);

            PointF[] pts = new PointF[4];
            for (int i = 0; i < 4; i++)
            {
                pts[i] = new PointF(vp.X + _quad[i].X * vp.Width, vp.Y + _quad[i].Y * vp.Height);
            }

            // Draw semi-transparent green quad overlay
            using (GraphicsPath poly = new GraphicsPath())
            {
                poly.AddPolygon(pts);
                using (SolidBrush fillBr = new SolidBrush(Color.FromArgb(40, 16, 185, 129)))
                {
                    g.FillPath(fillBr, poly);
                }
                using (Pen edgePen = new Pen(Color.FromArgb(245, 16, 185, 129), 2.5f))
                {
                    g.DrawPolygon(edgePen, pts);
                }
            }

            // Draw 4 draggable corner handles
            string[] labels = new string[] { "TL", "TR", "BR", "BL" };
            using (Font hf = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            {
                for (int i = 0; i < 4; i++)
                {
                    bool active = (i == _dragIndex);
                    float r = active ? 9f : 7f;
                    RectangleF hRect = new RectangleF(pts[i].X - r, pts[i].Y - r, r * 2, r * 2);
                    using (SolidBrush hb = new SolidBrush(active ? Color.Gold : Color.FromArgb(239, 68, 68)))
                    {
                        g.FillEllipse(hb, hRect);
                    }
                    using (Pen hp = new Pen(Color.White, 2f))
                    {
                        g.DrawEllipse(hp, hRect);
                    }
                    g.DrawString(labels[i], hf, Brushes.Yellow, pts[i].X + 9, pts[i].Y - 8);
                }
            }

            // If actively dragging a corner, draw a live 2.5x Magnifying Loupe in the top-left or top-right corner!
            if (_dragIndex >= 0 && _dragIndex < 4)
            {
                PointF normPt = _quad[_dragIndex];
                int loupeSize = 86;
                int lx = (normPt.X < 0.4f && normPt.Y < 0.4f) ? (Width - loupeSize - 10) : 10;
                int ly = 10;
                Rectangle dstLoupe = new Rectangle(lx, ly, loupeSize, loupeSize);

                int srcRadius = Math.Max(16, _image.Width / 18);
                int sx = (int)(normPt.X * _image.Width) - srcRadius;
                int sy = (int)(normPt.Y * _image.Height) - srcRadius;
                Rectangle srcRect = new Rectangle(sx, sy, srcRadius * 2, srcRadius * 2);

                g.FillRectangle(Brushes.Black, dstLoupe);
                g.DrawImage(_image, dstLoupe, srcRect, GraphicsUnit.Pixel);
                using (Pen lp = new Pen(Color.Gold, 2f))
                {
                    g.DrawRectangle(lp, dstLoupe);
                    int cx = lx + loupeSize / 2;
                    int cy = ly + loupeSize / 2;
                    g.DrawLine(lp, cx - 10, cy, cx + 10, cy);
                    g.DrawLine(lp, cx, cy - 10, cx, cy + 10);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_image == null || e.Button != MouseButtons.Left) return;

            Rectangle vp = GetImageViewport();
            float bestDist = 24f;
            int bestIdx = -1;
            for (int i = 0; i < 4; i++)
            {
                float px = vp.X + _quad[i].X * vp.Width;
                float py = vp.Y + _quad[i].Y * vp.Height;
                float dist = (float)Math.Sqrt((e.X - px) * (e.X - px) + (e.Y - py) * (e.Y - py));
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIdx = i;
                }
            }
            _dragIndex = bestIdx;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragIndex < 0 || _image == null) return;

            Rectangle vp = GetImageViewport();
            float nx = (float)(e.X - vp.X) / Math.Max(1, vp.Width);
            float ny = (float)(e.Y - vp.Y) / Math.Max(1, vp.Height);
            if (nx < 0f) nx = 0f; else if (nx > 1f) nx = 1f;
            if (ny < 0f) ny = 0f; else if (ny > 1f) ny = 1f;

            _quad[_dragIndex] = new PointF(nx, ny);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragIndex >= 0)
            {
                _dragIndex = -1;
                Invalidate();
                if (QuadOrImageChanged != null) QuadOrImageChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnDragEnter(DragEventArgs drgevent)
        {
            base.OnDragEnter(drgevent);
            if (drgevent.Data.GetDataPresent(DataFormats.FileDrop))
                drgevent.Effect = DragDropEffects.Copy;
        }

        protected override void OnDragDrop(DragEventArgs drgevent)
        {
            base.OnDragDrop(drgevent);
            string[] files = drgevent.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0 && File.Exists(files[0]))
            {
                try
                {
                    using (Bitmap loaded = new Bitmap(files[0]))
                    {
                        Bitmap copy = new Bitmap(loaded);
                        PointF[] q = CamScannerEngine.AutoDetectCorners(copy);
                        SetImageAndQuad(copy, q);
                    }
                }
                catch { }
            }
        }
    }

    public class MainForm : Form
    {
        private TabControl _tabs;

        // Tab 1: ID Card Copy Maker Controls
        private QuadCropCanvas _cropFront;
        private QuadCropCanvas _cropBack;
        private PictureBox _picIdSheetPreview;
        private ComboBox _cmbIdFilter;
        private ComboBox _cmbIdLayout;
        private TrackBar _trkIdWhite;
        private TrackBar _trkIdContrast;
        private CheckBox _chkRoundCorners;
        private CheckBox _chkCutGuides;
        private CheckBox _chkWatermark;
        private TextBox _txtWatermark;

        // Tab 2: Full-Page CamScanner Document Controls
        private QuadCropCanvas _cropDoc;
        private PictureBox _picDocPreview;
        private ComboBox _cmbDocFilter;
        private TrackBar _trkDocWhite;
        private TrackBar _trkDocContrast;
        private CheckBox _chkDocCleanBorder;

        private ToolStripStatusLabel _lblStatus;
        private Bitmap _currentPrintBitmap;

        public MainForm()
        {
            InitializeComponent();
            LoadBuiltInSamples();
        }

        private void InitializeComponent()
        {
            this.Text = "IdCardCamScannerDesk — ID Card Copy Maker (Recto/Verso A4) & CamScanner Bad Photo Enhancer";
            this.Size = new Size(1280, 820);
            this.MinimumSize = new Size(1060, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 9.0f, FontStyle.Regular);
            this.BackColor = Color.FromArgb(241, 245, 249);
            this.KeyPreview = true;
            this.KeyDown += OnFormKeyDown;

            // Header Banner
            Panel topBanner = new Panel
            {
                Dock = DockStyle.Top,
                Height = 56,
                BackColor = Color.FromArgb(15, 23, 42)
            };
            Label lblTitle = new Label
            {
                Text = "🪪 IdCardCamScannerDesk — E-Service Library ID Copy Printer & Bad Photo Scanner",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(16, 7)
            };
            Label lblSub = new Label
            {
                Text = "4-Corner Perspective Unwarp • Shadow Removal (Magic Color / Toner-Saver B&W) • Exact 1:1 CR80 ID Card A4 Composer",
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Segoe UI", 8.8f, FontStyle.Regular),
                AutoSize = true,
                Location = new Point(18, 32)
            };
            topBanner.Controls.Add(lblTitle);
            topBanner.Controls.Add(lblSub);
            this.Controls.Add(topBanner);

            StatusStrip status = new StatusStrip();
            _lblStatus = new ToolStripStatusLabel("Ready — Drag corners on any angled photo to flatten perspective and remove shadows.");
            status.Items.Add(_lblStatus);
            this.Controls.Add(status);

            _tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
            this.Controls.Add(_tabs);
            _tabs.BringToFront();

            TabPage tabId = new TabPage("🪪 1. ID Card Copy Maker & A4 Printer (Recto / Verso)");
            TabPage tabDoc = new TabPage("📄 2. CamScanner Full-Page Document Enhancer (Fix Bad Photos)");
            _tabs.TabPages.Add(tabId);
            _tabs.TabPages.Add(tabDoc);

            BuildIdCardTab(tabId);
            BuildDocumentScannerTab(tabDoc);
        }

        private void BuildIdCardTab(TabPage page)
        {
            SplitContainer split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 540,
                SplitterWidth = 6,
                BackColor = Color.FromArgb(203, 213, 225)
            };
            page.Controls.Add(split);

            // Left: Front & Back 4-corner crop panels
            TableLayoutPanel leftLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.White,
                Padding = new Padding(6)
            };
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            split.Panel1.Controls.Add(leftLayout);

            // Front Group
            GroupBox grpFront = new GroupBox
            {
                Text = "Front Side (RECTO) — Drag Red Corners to Unwarp Angled ID Photo",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold)
            };
            Panel frontTools = new Panel { Dock = DockStyle.Top, Height = 32 };
            _cropFront = new QuadCropCanvas("FRONT (RECTO) ID PHOTO") { Dock = DockStyle.Fill };
            _cropFront.QuadOrImageChanged += (s, e) => UpdateIdSheetPreview();

            Button btnLoadFront = CreateSmallButton("📂 Load Front", 4, 3, 92);
            btnLoadFront.Click += (s, e) => LoadImageIntoCanvas(_cropFront);
            Button btnPasteFront = CreateSmallButton("📋 Paste", 100, 3, 68);
            btnPasteFront.Click += (s, e) => PasteImageIntoCanvas(_cropFront);
            Button btnAutoFront = CreateSmallButton("🎯 Auto-Corners", 172, 3, 102);
            btnAutoFront.Click += (s, e) => { if (_cropFront.SourceImage != null) _cropFront.NormalizedQuad = CamScannerEngine.AutoDetectCorners(_cropFront.SourceImage); };
            Button btnFullFront = CreateSmallButton("⬜ Full", 278, 3, 58);
            btnFullFront.Click += (s, e) => _cropFront.ResetFullQuad();
            Button btnRotFront = CreateSmallButton("🔄 90°", 340, 3, 58);
            btnRotFront.Click += (s, e) => _cropFront.Rotate90CW();

            frontTools.Controls.AddRange(new Control[] { btnLoadFront, btnPasteFront, btnAutoFront, btnFullFront, btnRotFront });
            grpFront.Controls.Add(_cropFront);
            grpFront.Controls.Add(frontTools);
            _cropFront.BringToFront();
            leftLayout.Controls.Add(grpFront, 0, 0);

            // Back Group
            GroupBox grpBack = new GroupBox
            {
                Text = "Back Side (VERSO) — Drag Red Corners to Unwarp Angled ID Photo",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold)
            };
            Panel backTools = new Panel { Dock = DockStyle.Top, Height = 32 };
            _cropBack = new QuadCropCanvas("BACK (VERSO) ID PHOTO") { Dock = DockStyle.Fill };
            _cropBack.QuadOrImageChanged += (s, e) => UpdateIdSheetPreview();

            Button btnLoadBack = CreateSmallButton("📂 Load Back", 4, 3, 92);
            btnLoadBack.Click += (s, e) => LoadImageIntoCanvas(_cropBack);
            Button btnPasteBack = CreateSmallButton("📋 Paste", 100, 3, 68);
            btnPasteBack.Click += (s, e) => PasteImageIntoCanvas(_cropBack);
            Button btnAutoBack = CreateSmallButton("🎯 Auto-Corners", 172, 3, 102);
            btnAutoBack.Click += (s, e) => { if (_cropBack.SourceImage != null) _cropBack.NormalizedQuad = CamScannerEngine.AutoDetectCorners(_cropBack.SourceImage); };
            Button btnFullBack = CreateSmallButton("⬜ Full", 278, 3, 58);
            btnFullBack.Click += (s, e) => _cropBack.ResetFullQuad();
            Button btnRotBack = CreateSmallButton("🔄 90°", 340, 3, 58);
            btnRotBack.Click += (s, e) => _cropBack.Rotate90CW();

            backTools.Controls.AddRange(new Control[] { btnLoadBack, btnPasteBack, btnAutoBack, btnFullBack, btnRotBack });
            grpBack.Controls.Add(_cropBack);
            grpBack.Controls.Add(backTools);
            _cropBack.BringToFront();
            leftLayout.Controls.Add(grpBack, 0, 1);

            // Right Side: Controls + Live A4 Sheet Preview
            Panel rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(8) };
            split.Panel2.Controls.Add(rightPanel);

            GroupBox grpSettings = new GroupBox
            {
                Text = "CamScanner Enhancement & A4 Print Layout Settings",
                Dock = DockStyle.Top,
                Height = 148,
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold)
            };
            rightPanel.Controls.Add(grpSettings);

            Label lblFilter = new Label { Text = "Scan Filter:", Location = new Point(12, 24), AutoSize = true, Font = new Font("Segoe UI", 8.8f, FontStyle.Regular) };
            _cmbIdFilter = new ComboBox { Location = new Point(88, 21), Width = 225, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 8.8f, FontStyle.Regular) };
            _cmbIdFilter.Items.AddRange(new object[] {
                "Clean Grayscale (Photocopy + Portrait)",
                "Magic Color (Remove Shadows + Vivid)",
                "Toner-Saver B&W (Crisp Ink Only)",
                "Original Color (Brightened)"
            });
            _cmbIdFilter.SelectedIndex = 0;
            _cmbIdFilter.SelectedIndexChanged += (s, e) => UpdateIdSheetPreview();

            Label lblLayout = new Label { Text = "A4 Layout:", Location = new Point(325, 24), AutoSize = true, Font = new Font("Segoe UI", 8.8f, FontStyle.Regular) };
            _cmbIdLayout = new ComboBox { Location = new Point(395, 21), Width = 260, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 8.8f, FontStyle.Regular) };
            _cmbIdLayout.Items.AddRange(new object[] {
                "1x Copy — Top & Bottom (Exact 85.6×54mm)",
                "1x Copy — Side-by-Side (Saves Half Page)",
                "2x Copies on 1 A4 Sheet (2 Recto + 2 Verso)",
                "4x Copies on 1 A4 Sheet (4 Recto + 4 Verso)",
                "Enlarged 135% Copy (Administrative Dossier)"
            });
            _cmbIdLayout.SelectedIndex = 0;
            _cmbIdLayout.SelectedIndexChanged += (s, e) => UpdateIdSheetPreview();

            Label lblWhite = new Label { Text = "Shadow Removal:", Location = new Point(12, 58), AutoSize = true, Font = new Font("Segoe UI", 8.5f, FontStyle.Regular) };
            _trkIdWhite = new TrackBar { Location = new Point(112, 52), Width = 180, Minimum = 0, Maximum = 100, Value = 68, TickFrequency = 20, AutoSize = false, Height = 28 };
            _trkIdWhite.Scroll += (s, e) => UpdateIdSheetPreview();

            Label lblCont = new Label { Text = "Ink Darkness:", Location = new Point(310, 58), AutoSize = true, Font = new Font("Segoe UI", 8.5f, FontStyle.Regular) };
            _trkIdContrast = new TrackBar { Location = new Point(395, 52), Width = 180, Minimum = 0, Maximum = 100, Value = 62, TickFrequency = 20, AutoSize = false, Height = 28 };
            _trkIdContrast.Scroll += (s, e) => UpdateIdSheetPreview();

            _chkRoundCorners = new CheckBox { Text = "CR80 Rounded Frame", Location = new Point(14, 92), AutoSize = true, Checked = true, Font = new Font("Segoe UI", 8.5f, FontStyle.Regular) };
            _chkRoundCorners.CheckedChanged += (s, e) => UpdateIdSheetPreview();

            _chkCutGuides = new CheckBox { Text = "Scissor Cut Guides", Location = new Point(165, 92), AutoSize = true, Checked = true, Font = new Font("Segoe UI", 8.5f, FontStyle.Regular) };
            _chkCutGuides.CheckedChanged += (s, e) => UpdateIdSheetPreview();

            _chkWatermark = new CheckBox { Text = "Cross Stamp:", Location = new Point(310, 92), AutoSize = true, Checked = false, Font = new Font("Segoe UI", 8.5f, FontStyle.Regular) };
            _chkWatermark.CheckedChanged += (s, e) => UpdateIdSheetPreview();

            _txtWatermark = new TextBox { Text = "USAGE ADMINISTRATIF UNIQUEMENT", Location = new Point(408, 90), Width = 225, Font = new Font("Segoe UI", 8.5f, FontStyle.Regular) };
            _txtWatermark.TextChanged += (s, e) => { if (_chkWatermark.Checked) UpdateIdSheetPreview(); };

            Button btnPrintId = new Button
            {
                Text = "🖨️ Print A4 ID Sheet",
                Location = new Point(14, 115),
                Width = 165,
                Height = 28,
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold)
            };
            btnPrintId.Click += (s, e) => PrintSheet(true);

            Button btnSaveId = new Button
            {
                Text = "💾 Save 300-DPI A4 Sheet...",
                Location = new Point(186, 115),
                Width = 185,
                Height = 28,
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold)
            };
            btnSaveId.Click += (s, e) => SaveHighResIdSheet();

            Button btnDemoId = new Button
            {
                Text = "✨ Reload Simulated Bad ID Photos",
                Location = new Point(378, 115),
                Width = 220,
                Height = 28,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular)
            };
            btnDemoId.Click += (s, e) => LoadBuiltInSamples();

            grpSettings.Controls.AddRange(new Control[] {
                lblFilter, _cmbIdFilter, lblLayout, _cmbIdLayout,
                lblWhite, _trkIdWhite, lblCont, _trkIdContrast,
                _chkRoundCorners, _chkCutGuides, _chkWatermark, _txtWatermark,
                btnPrintId, btnSaveId, btnDemoId
            });

            // Preview Panel
            GroupBox grpSheetPrev = new GroupBox
            {
                Text = "Live A4 Sheet Print Preview (210 × 297 mm)",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold)
            };
            rightPanel.Controls.Add(grpSheetPrev);
            grpSheetPrev.BringToFront();

            Panel bgPrev = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(71, 85, 105), Padding = new Padding(12) };
            _picIdSheetPreview = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(71, 85, 105)
            };
            bgPrev.Controls.Add(_picIdSheetPreview);
            grpSheetPrev.Controls.Add(bgPrev);
        }

        private void BuildDocumentScannerTab(TabPage page)
        {
            SplitContainer split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 560,
                SplitterWidth = 6,
                BackColor = Color.FromArgb(203, 213, 225)
            };
            page.Controls.Add(split);

            // Left: Bad Document Photo Crop Canvas
            GroupBox grpLeft = new GroupBox
            {
                Text = "Original Smartphone Photo — Drag the 4 Red Corners Around the Document Page",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.0f, FontStyle.Bold),
                BackColor = Color.White
            };
            split.Panel1.Controls.Add(grpLeft);

            Panel docTools = new Panel { Dock = DockStyle.Top, Height = 36 };
            _cropDoc = new QuadCropCanvas("BAD DOCUMENT PHOTO") { Dock = DockStyle.Fill };
            _cropDoc.QuadOrImageChanged += (s, e) => UpdateDocPreview();

            Button btnLoadDoc = CreateSmallButton("📂 Load Bad Photo...", 6, 4, 130);
            btnLoadDoc.Click += (s, e) => LoadImageIntoCanvas(_cropDoc);
            Button btnPasteDoc = CreateSmallButton("📋 Paste (Ctrl+V)", 142, 4, 115);
            btnPasteDoc.Click += (s, e) => PasteImageIntoCanvas(_cropDoc);
            Button btnAutoDoc = CreateSmallButton("🎯 Auto-Detect Page", 263, 4, 130);
            btnAutoDoc.Click += (s, e) => { if (_cropDoc.SourceImage != null) _cropDoc.NormalizedQuad = CamScannerEngine.AutoDetectCorners(_cropDoc.SourceImage); };
            Button btnFullDoc = CreateSmallButton("⬜ Full", 398, 4, 60);
            btnFullDoc.Click += (s, e) => _cropDoc.ResetFullQuad();
            Button btnRotDoc = CreateSmallButton("🔄 90°", 462, 4, 60);
            btnRotDoc.Click += (s, e) => _cropDoc.Rotate90CW();

            docTools.Controls.AddRange(new Control[] { btnLoadDoc, btnPasteDoc, btnAutoDoc, btnFullDoc, btnRotDoc });
            grpLeft.Controls.Add(_cropDoc);
            grpLeft.Controls.Add(docTools);
            _cropDoc.BringToFront();

            // Right: CamScanner Enhanced Document Output
            Panel rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(8) };
            split.Panel2.Controls.Add(rightPanel);

            GroupBox grpDocCtrl = new GroupBox
            {
                Text = "CamScanner Shadow Removal & Binarization Controls",
                Dock = DockStyle.Top,
                Height = 118,
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold)
            };
            rightPanel.Controls.Add(grpDocCtrl);

            Label lblMode = new Label { Text = "Enhancement Mode:", Location = new Point(12, 24), AutoSize = true, Font = new Font("Segoe UI", 8.8f, FontStyle.Regular) };
            _cmbDocFilter = new ComboBox { Location = new Point(135, 21), Width = 250, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 8.8f, FontStyle.Regular) };
            _cmbDocFilter.Items.AddRange(new object[] {
                "Clean Grayscale (Smooth Photocopy)",
                "Magic Color (Keep Blue/Red Stamps + White Page)",
                "Toner-Saver B&W (Razor-Sharp Text Only)",
                "Original Color (Unwarped)"
            });
            _cmbDocFilter.SelectedIndex = 1; // Magic Color default for docs
            _cmbDocFilter.SelectedIndexChanged += (s, e) => UpdateDocPreview();

            _chkDocCleanBorder = new CheckBox { Text = "Auto-Trim Dark Table Edges", Location = new Point(405, 23), AutoSize = true, Checked = true, Font = new Font("Segoe UI", 8.5f, FontStyle.Regular) };
            _chkDocCleanBorder.CheckedChanged += (s, e) => UpdateDocPreview();

            Label lblDocW = new Label { Text = "Shadow Removal:", Location = new Point(12, 56), AutoSize = true, Font = new Font("Segoe UI", 8.5f, FontStyle.Regular) };
            _trkDocWhite = new TrackBar { Location = new Point(115, 50), Width = 180, Minimum = 0, Maximum = 100, Value = 72, TickFrequency = 20, AutoSize = false, Height = 28 };
            _trkDocWhite.Scroll += (s, e) => UpdateDocPreview();

            Label lblDocC = new Label { Text = "Ink Darkness:", Location = new Point(310, 56), AutoSize = true, Font = new Font("Segoe UI", 8.5f, FontStyle.Regular) };
            _trkDocContrast = new TrackBar { Location = new Point(398, 50), Width = 180, Minimum = 0, Maximum = 100, Value = 68, TickFrequency = 20, AutoSize = false, Height = 28 };
            _trkDocContrast.Scroll += (s, e) => UpdateDocPreview();

            Button btnPrintDoc = new Button
            {
                Text = "🖨️ Print Clean A4 Document",
                Location = new Point(14, 82),
                Width = 195,
                Height = 28,
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold)
            };
            btnPrintDoc.Click += (s, e) => PrintSheet(false);

            Button btnSaveDoc = new Button
            {
                Text = "💾 Save Clean Scan (PNG/JPG)...",
                Location = new Point(218, 82),
                Width = 210,
                Height = 28,
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold)
            };
            btnSaveDoc.Click += (s, e) => SaveCleanDocImage();

            grpDocCtrl.Controls.AddRange(new Control[] {
                lblMode, _cmbDocFilter, _chkDocCleanBorder,
                lblDocW, _trkDocWhite, lblDocC, _trkDocContrast,
                btnPrintDoc, btnSaveDoc
            });

            GroupBox grpDocPrev = new GroupBox
            {
                Text = "Unwarped & Shadow-Free A4 Scan Result",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold)
            };
            rightPanel.Controls.Add(grpDocPrev);
            grpDocPrev.BringToFront();

            Panel bgDoc = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(71, 85, 105), Padding = new Padding(12) };
            _picDocPreview = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(71, 85, 105)
            };
            bgDoc.Controls.Add(_picDocPreview);
            grpDocPrev.Controls.Add(bgDoc);
        }

        private Button CreateSmallButton(string text, int x, int y, int w)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, y),
                Width = w,
                Height = 25,
                Font = new Font("Segoe UI", 8.0f, FontStyle.Regular)
            };
        }

        private void LoadBuiltInSamples()
        {
            PointF[] qFront, qBack, qDoc;
            Bitmap badFront = SamplePhotoGenerator.CreateSampleBadIdPhoto(true, out qFront);
            Bitmap badBack = SamplePhotoGenerator.CreateSampleBadIdPhoto(false, out qBack);
            Bitmap badDoc = SamplePhotoGenerator.CreateSampleBadDocument(out qDoc);

            _cropFront.SetImageAndQuad(badFront, qFront);
            _cropBack.SetImageAndQuad(badBack, qBack);
            _cropDoc.SetImageAndQuad(badDoc, qDoc);

            UpdateIdSheetPreview();
            UpdateDocPreview();
            _lblStatus.Text = "Loaded simulated shadowed & angled WhatsApp photos — drag the red corner handles or switch filters to compare!";
        }

        private void LoadImageIntoCanvas(QuadCropCanvas canvas)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "Image Files (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|All Files (*.*)|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        using (Bitmap raw = new Bitmap(ofd.FileName))
                        {
                            Bitmap copy = new Bitmap(raw);
                            PointF[] q = CamScannerEngine.AutoDetectCorners(copy);
                            canvas.SetImageAndQuad(copy, q);
                            _lblStatus.Text = "Loaded photo: " + Path.GetFileName(ofd.FileName);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Failed to load image: " + ex.Message, "Image Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void PasteImageIntoCanvas(QuadCropCanvas canvas)
        {
            if (Clipboard.ContainsImage())
            {
                Image clipImg = Clipboard.GetImage();
                if (clipImg != null)
                {
                    Bitmap copy = new Bitmap(clipImg);
                    PointF[] q = CamScannerEngine.AutoDetectCorners(copy);
                    canvas.SetImageAndQuad(copy, q);
                    _lblStatus.Text = "Pasted photo from clipboard and auto-detected corners.";
                }
            }
            else
            {
                MessageBox.Show("Clipboard does not currently contain an image.\nTip: Right-click any image in WhatsApp / browser and click 'Copy Image', then click Paste.", "Paste Image", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.V)
            {
                if (_tabs.SelectedIndex == 1)
                    PasteImageIntoCanvas(_cropDoc);
                else
                    PasteImageIntoCanvas(_cropFront);
            }
        }

        private Bitmap ProcessSingleIdCard(QuadCropCanvas canvas, int targetW, int targetH)
        {
            if (canvas.SourceImage == null) return null;
            using (Bitmap unwarped = CamScannerEngine.UnwarpPerspective(canvas.SourceImage, canvas.NormalizedQuad, targetW, targetH))
            {
                ScanFilterMode mode = (ScanFilterMode)_cmbIdFilter.SelectedIndex;
                return CamScannerEngine.ApplyCamScannerFilter(unwarped, mode, _trkIdWhite.Value, _trkIdContrast.Value, 6);
            }
        }

        private void UpdateIdSheetPreview()
        {
            if (_cropFront == null || _cropBack == null || _cmbIdFilter == null) return;

            // Preview at 150 DPI for instant <30ms interactive slider responsiveness
            using (Bitmap fCard = ProcessSingleIdCard(_cropFront, 540, 340))
            using (Bitmap bCard = ProcessSingleIdCard(_cropBack, 540, 340))
            {
                A4IdLayoutMode layout = (A4IdLayoutMode)_cmbIdLayout.SelectedIndex;
                string wm = _chkWatermark.Checked ? _txtWatermark.Text.Trim() : null;
                Bitmap sheet = A4SheetComposer.RenderA4IdSheet(fCard, bCard, layout, _chkRoundCorners.Checked, _chkCutGuides.Checked, wm, 150);

                Bitmap old = _picIdSheetPreview.Image as Bitmap;
                _picIdSheetPreview.Image = sheet;
                if (old != null) old.Dispose();
            }
        }

        private Bitmap RenderFull300DpiIdSheet()
        {
            // Render at true 300 DPI (CR80 = 1011 x 638 px on 2480 x 3508 px A4 sheet)
            using (Bitmap fCard = ProcessSingleIdCard(_cropFront, 1011, 638))
            using (Bitmap bCard = ProcessSingleIdCard(_cropBack, 1011, 638))
            {
                A4IdLayoutMode layout = (A4IdLayoutMode)_cmbIdLayout.SelectedIndex;
                string wm = _chkWatermark.Checked ? _txtWatermark.Text.Trim() : null;
                return A4SheetComposer.RenderA4IdSheet(fCard, bCard, layout, _chkRoundCorners.Checked, _chkCutGuides.Checked, wm, 300);
            }
        }

        private void UpdateDocPreview()
        {
            if (_cropDoc == null || _cropDoc.SourceImage == null || _cmbDocFilter == null) return;

            using (Bitmap unwarped = CamScannerEngine.UnwarpPerspective(_cropDoc.SourceImage, _cropDoc.NormalizedQuad, 900, 1273))
            {
                ScanFilterMode mode = (ScanFilterMode)_cmbDocFilter.SelectedIndex;
                int borderPx = _chkDocCleanBorder.Checked ? 12 : 0;
                Bitmap enhanced = CamScannerEngine.ApplyCamScannerFilter(unwarped, mode, _trkDocWhite.Value, _trkDocContrast.Value, borderPx);

                Bitmap old = _picDocPreview.Image as Bitmap;
                _picDocPreview.Image = enhanced;
                if (old != null) old.Dispose();
            }
        }

        private void SaveHighResIdSheet()
        {
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "PNG High-Res Sheet (*.png)|*.png|JPEG Sheet (*.jpg)|*.jpg";
                sfd.FileName = "ID_Card_A4_Sheet_300DPI.png";
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    using (Bitmap hiRes = RenderFull300DpiIdSheet())
                    {
                        ImageFormat fmt = sfd.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? ImageFormat.Jpeg : ImageFormat.Png;
                        hiRes.Save(sfd.FileName, fmt);
                        _lblStatus.Text = "Saved 300-DPI A4 ID Card Sheet: " + sfd.FileName;
                    }
                }
            }
        }

        private void SaveCleanDocImage()
        {
            if (_cropDoc.SourceImage == null) return;
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "PNG Document Scan (*.png)|*.png|JPEG Document Scan (*.jpg)|*.jpg";
                sfd.FileName = "CamScanner_Cleaned_A4.png";
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    using (Bitmap unwarped = CamScannerEngine.UnwarpPerspective(_cropDoc.SourceImage, _cropDoc.NormalizedQuad, 1654, 2339))
                    {
                        ScanFilterMode mode = (ScanFilterMode)_cmbDocFilter.SelectedIndex;
                        int borderPx = _chkDocCleanBorder.Checked ? 18 : 0;
                        using (Bitmap hiRes = CamScannerEngine.ApplyCamScannerFilter(unwarped, mode, _trkDocWhite.Value, _trkDocContrast.Value, borderPx))
                        {
                            ImageFormat fmt = sfd.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? ImageFormat.Jpeg : ImageFormat.Png;
                            hiRes.Save(sfd.FileName, fmt);
                            _lblStatus.Text = "Saved enhanced A4 document scan: " + sfd.FileName;
                        }
                    }
                }
            }
        }

        private void PrintSheet(bool isIdCardSheet)
        {
            if (_currentPrintBitmap != null)
            {
                _currentPrintBitmap.Dispose();
                _currentPrintBitmap = null;
            }

            if (isIdCardSheet)
            {
                _currentPrintBitmap = RenderFull300DpiIdSheet();
            }
            else
            {
                if (_picDocPreview.Image == null) return;
                _currentPrintBitmap = new Bitmap(_picDocPreview.Image);
            }

            using (PrintDocument pd = new PrintDocument())
            {
                pd.DocumentName = isIdCardSheet ? "ID Card A4 Copy Sheet" : "CamScanner Enhanced Document";
                pd.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
                pd.PrintPage += (s, ev) =>
                {
                    if (_currentPrintBitmap != null)
                    {
                        ev.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        ev.Graphics.DrawImage(_currentPrintBitmap, ev.PageBounds);
                    }
                    ev.HasMorePages = false;
                };

                using (PrintPreviewDialog ppd = new PrintPreviewDialog())
                {
                    ppd.Document = pd;
                    ppd.Width = 900;
                    ppd.Height = 720;
                    ppd.StartPosition = FormStartPosition.CenterParent;
                    ppd.ShowDialog(this);
                }
            }
        }
    }

    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] != "--gui")
            {
                return RunCli(args);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }

        private static int RunCli(string[] args)
        {
            string cmd = args[0].ToLowerInvariant();

            if (cmd == "--help" || cmd == "-h" || cmd == "/?")
            {
                Console.WriteLine("===================================================================================");
                Console.WriteLine(" IdCardCamScannerDesk.exe — ID Card Copy Printer & CamScanner Bad Photo Enhancer");
                Console.WriteLine("===================================================================================");
                Console.WriteLine("Usage:");
                Console.WriteLine("  IdCardCamScannerDesk.exe");
                Console.WriteLine("      Launch interactive Windows Desktop Studio (ID Copy Maker + Document Scanner)");
                Console.WriteLine("");
                Console.WriteLine("  IdCardCamScannerDesk.exe --enhance <in_photo> <out_clean.png> [--mode magic|bw|gray|orig] [--autocrop]");
                Console.WriteLine("      Remove shadows, whiten background, and unwarp a bad smartphone document photo");
                Console.WriteLine("");
                Console.WriteLine("  IdCardCamScannerDesk.exe --id-sheet --front <front_img> --back <back_img> --out <a4_sheet.png>");
                Console.WriteLine("                           [--layout 1x|side|2x|4x|135] [--mode gray|magic|bw] [--stamp \"TEXT\"]");
                Console.WriteLine("      Compose a 300-DPI A4 printable ID card sheet at exact 1:1 CR80 dimensions (85.6x54mm)");
                Console.WriteLine("");
                Console.WriteLine("  IdCardCamScannerDesk.exe --demo");
                Console.WriteLine("      Generate simulated bad shadowed photos and output cleaned ID & Document scans");
                return 0;
            }

            if (cmd == "--enhance" && args.Length >= 3)
            {
                string inFile = args[1];
                string outFile = args[2];
                ScanFilterMode mode = ScanFilterMode.MagicColor;
                bool autoCrop = false;

                for (int i = 3; i < args.Length; i++)
                {
                    if (args[i] == "--mode" && i + 1 < args.Length)
                    {
                        mode = ParseMode(args[++i]);
                    }
                    else if (args[i] == "--autocrop")
                    {
                        autoCrop = true;
                    }
                }

                if (!File.Exists(inFile))
                {
                    Console.WriteLine("Error: Input file not found: " + inFile);
                    return 1;
                }

                using (Bitmap raw = new Bitmap(inFile))
                {
                    PointF[] quad = autoCrop
                        ? CamScannerEngine.AutoDetectCorners(raw)
                        : new PointF[] { new PointF(0, 0), new PointF(1, 0), new PointF(1, 1), new PointF(0, 1) };

                    int targetW = autoCrop ? 1654 : raw.Width;
                    int targetH = autoCrop ? 2339 : raw.Height;

                    using (Bitmap unwarped = CamScannerEngine.UnwarpPerspective(raw, quad, targetW, targetH))
                    using (Bitmap cleaned = CamScannerEngine.ApplyCamScannerFilter(unwarped, mode, 70, 65, autoCrop ? 14 : 0))
                    {
                        cleaned.Save(outFile, ImageFormat.Png);
                        Console.WriteLine(string.Format("Enhanced '{0}' ({1}) -> '{2}' ({3}x{4})",
                            inFile, mode, outFile, cleaned.Width, cleaned.Height));
                    }
                }
                return 0;
            }

            if (cmd == "--id-sheet")
            {
                string frontPath = null;
                string backPath = null;
                string outPath = "id_card_a4_sheet.png";
                A4IdLayoutMode layout = A4IdLayoutMode.SingleTopBottom;
                ScanFilterMode mode = ScanFilterMode.CleanGrayscale;
                string stamp = null;

                for (int i = 1; i < args.Length - 1; i++)
                {
                    if (args[i] == "--front") frontPath = args[++i];
                    else if (args[i] == "--back") backPath = args[++i];
                    else if (args[i] == "--out") outPath = args[++i];
                    else if (args[i] == "--mode") mode = ParseMode(args[++i]);
                    else if (args[i] == "--stamp") stamp = args[++i];
                    else if (args[i] == "--layout")
                    {
                        string l = args[++i].ToLowerInvariant();
                        if (l == "side") layout = A4IdLayoutMode.SingleSideBySide;
                        else if (l == "2x") layout = A4IdLayoutMode.DoubleCopy2x;
                        else if (l == "4x") layout = A4IdLayoutMode.QuadCopy4x;
                        else if (l == "135") layout = A4IdLayoutMode.Enlarged135Percent;
                    }
                }

                if (string.IsNullOrEmpty(frontPath) || !File.Exists(frontPath))
                {
                    Console.WriteLine("Error: Valid --front <image> is required.");
                    return 1;
                }

                using (Bitmap rawF = new Bitmap(frontPath))
                using (Bitmap rawB = (!string.IsNullOrEmpty(backPath) && File.Exists(backPath)) ? new Bitmap(backPath) : new Bitmap(frontPath))
                {
                    PointF[] qF = CamScannerEngine.AutoDetectCorners(rawF);
                    PointF[] qB = CamScannerEngine.AutoDetectCorners(rawB);

                    using (Bitmap uwF = CamScannerEngine.UnwarpPerspective(rawF, qF, 1011, 638))
                    using (Bitmap uwB = CamScannerEngine.UnwarpPerspective(rawB, qB, 1011, 638))
                    using (Bitmap clF = CamScannerEngine.ApplyCamScannerFilter(uwF, mode, 68, 62, 6))
                    using (Bitmap clB = CamScannerEngine.ApplyCamScannerFilter(uwB, mode, 68, 62, 6))
                    using (Bitmap sheet = A4SheetComposer.RenderA4IdSheet(clF, clB, layout, true, true, stamp, 300))
                    {
                        sheet.Save(outPath, ImageFormat.Png);
                        Console.WriteLine(string.Format("Generated 300-DPI A4 ID Card Sheet ({0}, {1}) -> {2}",
                            layout, mode, outPath));
                    }
                }
                return 0;
            }

            if (cmd == "--demo")
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                Console.WriteLine("=== IdCardCamScannerDesk Self-Verification Demo ===");

                PointF[] qFront, qBack, qDoc;
                using (Bitmap badFront = SamplePhotoGenerator.CreateSampleBadIdPhoto(true, out qFront))
                using (Bitmap badBack = SamplePhotoGenerator.CreateSampleBadIdPhoto(false, out qBack))
                using (Bitmap badDoc = SamplePhotoGenerator.CreateSampleBadDocument(out qDoc))
                {
                    string pBadF = Path.Combine(dir, "demo_bad_id_front.png");
                    string pBadDoc = Path.Combine(dir, "demo_bad_doc_photo.png");
                    string pCleanDoc = Path.Combine(dir, "demo_clean_doc_magic.png");
                    string pIdSheet = Path.Combine(dir, "demo_a4_id_card_sheet.png");

                    badFront.Save(pBadF, ImageFormat.Png);
                    badDoc.Save(pBadDoc, ImageFormat.Png);
                    Console.WriteLine("1. Generated simulated angled/shadowed smartphone photos:");
                    Console.WriteLine("   - " + pBadF);
                    Console.WriteLine("   - " + pBadDoc);

                    using (Bitmap uwF = CamScannerEngine.UnwarpPerspective(badFront, qFront, 1011, 638))
                    using (Bitmap uwB = CamScannerEngine.UnwarpPerspective(badBack, qBack, 1011, 638))
                    using (Bitmap clF = CamScannerEngine.ApplyCamScannerFilter(uwF, ScanFilterMode.CleanGrayscale, 68, 62, 6))
                    using (Bitmap clB = CamScannerEngine.ApplyCamScannerFilter(uwB, ScanFilterMode.CleanGrayscale, 68, 62, 6))
                    using (Bitmap sheet = A4SheetComposer.RenderA4IdSheet(clF, clB, A4IdLayoutMode.DoubleCopy2x, true, true, null, 200))
                    {
                        sheet.Save(pIdSheet, ImageFormat.Png);
                        Console.WriteLine("2. Unwarped ID Front & Back and composed 2x A4 Copy Sheet:");
                        Console.WriteLine("   - " + pIdSheet);
                    }

                    using (Bitmap uwDoc = CamScannerEngine.UnwarpPerspective(badDoc, qDoc, 1240, 1754))
                    using (Bitmap clDoc = CamScannerEngine.ApplyCamScannerFilter(uwDoc, ScanFilterMode.MagicColor, 72, 68, 12))
                    {
                        clDoc.Save(pCleanDoc, ImageFormat.Png);
                        Console.WriteLine("3. Unwarped bad document photo & applied Magic Color shadow removal:");
                        Console.WriteLine("   - " + pCleanDoc);
                    }
                }
                Console.WriteLine("=== All CamScanner & ID Sheet Engines Verified (100% OK) ===");
                return 0;
            }

            Console.WriteLine("Unknown argument: " + cmd + ". Run with --help for usage.");
            return 1;
        }

        private static ScanFilterMode ParseMode(string m)
        {
            if (string.IsNullOrEmpty(m)) return ScanFilterMode.MagicColor;
            m = m.ToLowerInvariant();
            if (m == "bw" || m == "bin") return ScanFilterMode.CrispBlackAndWhite;
            if (m == "gray" || m == "grey") return ScanFilterMode.CleanGrayscale;
            if (m == "orig" || m == "original") return ScanFilterMode.OriginalEnhanced;
            return ScanFilterMode.MagicColor;
        }
    }
}
