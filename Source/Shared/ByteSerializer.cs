using System;
using System.Collections.Generic;
using System.Text;

namespace HybridShared
{
    /// <summary>
    /// 바이너리 쓰기 유틸리티
    /// MP ByteWriter 패턴 적용
    /// </summary>
    public class ByteWriter
    {
        private List<byte> data = new List<byte>(256);
        
        public byte[] ToArray() => data.ToArray();
        public int Length => data.Count;
        
        public void Clear() => data.Clear();
        
        // ===== 기본 타입 =====
        
        public void WriteBool(bool value) => data.Add((byte)(value ? 1 : 0));
        
        public void WriteByte(byte value) => data.Add(value);
        
        public void WriteSByte(sbyte value) => data.Add((byte)value);
        
        public void WriteShort(short value)
        {
            data.Add((byte)value);
            data.Add((byte)(value >> 8));
        }
        
        public void WriteUShort(ushort value)
        {
            data.Add((byte)value);
            data.Add((byte)(value >> 8));
        }
        
        public void WriteInt(int value)
        {
            data.Add((byte)value);
            data.Add((byte)(value >> 8));
            data.Add((byte)(value >> 16));
            data.Add((byte)(value >> 24));
        }
        
        public void WriteUInt(uint value)
        {
            data.Add((byte)value);
            data.Add((byte)(value >> 8));
            data.Add((byte)(value >> 16));
            data.Add((byte)(value >> 24));
        }
        
        public void WriteLong(long value)
        {
            WriteInt((int)value);
            WriteInt((int)(value >> 32));
        }
        
        public void WriteULong(ulong value)
        {
            WriteUInt((uint)value);
            WriteUInt((uint)(value >> 32));
        }
        
        public void WriteFloat(float value)
        {
            var bytes = BitConverter.GetBytes(value);
            data.AddRange(bytes);
        }
        
        public void WriteDouble(double value)
        {
            var bytes = BitConverter.GetBytes(value);
            data.AddRange(bytes);
        }
        
        // ===== 문자열 =====
        
        public void WriteString(string value)
        {
            if (value == null)
            {
                WriteInt(-1);
                return;
            }
            
            var bytes = Encoding.UTF8.GetBytes(value);
            WriteInt(bytes.Length);
            data.AddRange(bytes);
        }
        
        // ===== 배열 =====
        
        public void WriteBytes(byte[] bytes)
        {
            if (bytes == null)
            {
                WriteInt(-1);
                return;
            }
            
            WriteInt(bytes.Length);
            data.AddRange(bytes);
        }
        
        public void WriteIntArray(int[] arr)
        {
            if (arr == null)
            {
                WriteInt(-1);
                return;
            }
            
            WriteInt(arr.Length);
            foreach (var v in arr)
                WriteInt(v);
        }
    }
    
    /// <summary>
    /// 바이너리 읽기 유틸리티
    /// MP ByteReader 패턴 적용
    /// </summary>
    public class ByteReader
    {
        private byte[] data;
        private int pos;
        
        public ByteReader(byte[] data)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            this.pos = 0;
        }
        
        public int Position => pos;
        public int Length => data.Length;
        public bool HasMore => pos < data.Length;
        
        // ===== 기본 타입 =====
        
        public bool ReadBool() => data[pos++] != 0;
        
        public byte ReadByte() => data[pos++];
        
        public sbyte ReadSByte() => (sbyte)data[pos++];
        
        public short ReadShort()
        {
            var value = (short)(data[pos] | (data[pos + 1] << 8));
            pos += 2;
            return value;
        }
        
        public ushort ReadUShort()
        {
            var value = (ushort)(data[pos] | (data[pos + 1] << 8));
            pos += 2;
            return value;
        }
        
        public int ReadInt()
        {
            var value = data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24);
            pos += 4;
            return value;
        }
        
        public uint ReadUInt()
        {
            var value = (uint)(data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24));
            pos += 4;
            return value;
        }
        
        public long ReadLong()
        {
            var low = (uint)ReadInt();
            var high = (uint)ReadInt();
            return (long)low | ((long)high << 32);
        }
        
        public ulong ReadULong()
        {
            var low = ReadUInt();
            var high = ReadUInt();
            return low | ((ulong)high << 32);
        }
        
        public float ReadFloat()
        {
            var value = BitConverter.ToSingle(data, pos);
            pos += 4;
            return value;
        }
        
        public double ReadDouble()
        {
            var value = BitConverter.ToDouble(data, pos);
            pos += 8;
            return value;
        }
        
        // ===== 문자열 =====
        
        public string ReadString()
        {
            var length = ReadInt();
            if (length < 0) return null;
            if (length == 0) return "";
            
            var value = Encoding.UTF8.GetString(data, pos, length);
            pos += length;
            return value;
        }
        
        // ===== 배열 =====
        
        public byte[] ReadBytes()
        {
            var length = ReadInt();
            if (length < 0) return null;
            if (length == 0) return Array.Empty<byte>();
            
            var bytes = new byte[length];
            Array.Copy(data, pos, bytes, 0, length);
            pos += length;
            return bytes;
        }
        
        public int[] ReadIntArray()
        {
            var length = ReadInt();
            if (length < 0) return null;
            if (length == 0) return Array.Empty<int>();
            
            var arr = new int[length];
            for (int i = 0; i < length; i++)
                arr[i] = ReadInt();
            return arr;
        }
    }
}
