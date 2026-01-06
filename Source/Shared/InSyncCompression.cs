using System;
using System.IO;
using System.IO.Compression;

namespace HybridShared
{
    /// <summary>
    /// 데이터 압축/해제 유틸리티
    /// MP SaveCompression 패턴 적용
    /// </summary>
    public static class InSyncCompression
    {
        /// <summary>
        /// 데이터 압축 (GZip)
        /// </summary>
        public static byte[] Compress(byte[] data)
        {
            if (data == null || data.Length == 0)
                return data;
            
            try
            {
                using (var output = new MemoryStream())
                {
                    using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
                    {
                        gzip.Write(data, 0, data.Length);
                    }
                    return output.ToArray();
                }
            }
            catch
            {
                // 압축 실패 시 원본 반환
                return data;
            }
        }
        
        /// <summary>
        /// 데이터 해제 (GZip)
        /// </summary>
        public static byte[] Decompress(byte[] compressedData)
        {
            if (compressedData == null || compressedData.Length == 0)
                return compressedData;
            
            try
            {
                using (var input = new MemoryStream(compressedData))
                using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    gzip.CopyTo(output);
                    return output.ToArray();
                }
            }
            catch
            {
                // 해제 실패 시 원본 반환 (압축되지 않은 데이터일 수 있음)
                return compressedData;
            }
        }
        
        /// <summary>
        /// 압축된 데이터를 Base64로 인코딩
        /// </summary>
        public static string CompressAndEncode(byte[] data)
        {
            if (data == null || data.Length == 0)
                return null;
            
            var compressed = Compress(data);
            return Convert.ToBase64String(compressed);
        }
        
        /// <summary>
        /// Base64 디코딩 후 압축 해제
        /// </summary>
        public static byte[] DecodeAndDecompress(string encoded)
        {
            if (string.IsNullOrEmpty(encoded))
                return null;
            
            try
            {
                var compressed = Convert.FromBase64String(encoded);
                return Decompress(compressed);
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// 압축 비율 계산 (디버깅용)
        /// </summary>
        public static double GetCompressionRatio(byte[] original, byte[] compressed)
        {
            if (original == null || original.Length == 0)
                return 1.0;
            
            return (double)compressed.Length / original.Length;
        }
    }
}
