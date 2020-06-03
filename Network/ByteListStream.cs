
//#define Server_Side
//#define Client_Side
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace uNet2.Network {
    /// <summary>
    /// 提供一个可变 指定 N长度的数组组合 存放byte[]数据内容对象
    /// 用于连续申请内存数据,灵活，但是内存碎片较多
    /// </summary>
    public class ByteListStream : List<ArraySegment<byte>>, IDisposable {
        static void test() {
            //从缓存中取buf 用完回收 最大支持64K缓存数据
            Random rn = new Random();
            ByteListStream bs = new ByteListStream();
            for (int i = 0; i < 10; i++) {
                //从缓存池中申请byte数组
                var v128 = ByteListPoolMgr.AllocBuf(ByteListPool.BufferLevel.b128);
                rn.NextBytes(v128);
                //大数据分片放入ByteListStream
                bs.Add(new ArraySegment<byte>(v128));
            }
            //读取数据
            byte[] buf = bs.getBytes(256);
            //覆盖读取数据
            bs.getBytes(ref buf, 0, 256);
            //销毁并回收关联的内存
            bs.Dispose();
            
        }
        /// <summary>
        /// 数据取出后不会从当前列表中移除
        /// </summary>
        /// <param name="len"></param>
        /// <returns></returns>
        public byte[] peekBytes(int len) {
            if (available() < len) return null;
            if (Count < 1) return null;
            byte[] data = new byte[len];
            int offset = 0;
            for (int i = 0; i < this.Count; i++) {
                int leave = len - offset;
                if (leave <= 0) break;
                int readL = (leave > this[i].Count) ? this[i].Count : leave;
                Array.Copy(this[i].Array, this[i].Offset, data, offset, readL);
                offset += readL;
            }
            // 
            return data;
        }
        /// <summary>
        /// 数据取出后不会从当前列表中移除
        /// </summary>
        /// <param name="fill2Data">填充数组，外面保证其剩余长度有效性</param>
        /// <param name="fill2BuffOffset"></param>
        /// <param name="len">外面保证取长度的有效性</param>
        public void peekBytes(ref byte[] fill2Data, int fill2BuffOffset, int len) {
            if (available() < len) return;
            if (fill2Data.Length - fill2BuffOffset < len) return;
            if (Count < 1) return;
            int offset = 0;
            for (int i = 0; i < this.Count; i++) {
                int leave = len - offset;
                if (leave <= 0) break;
                int readL = (leave > this[i].Count) ? this[i].Count : leave;
                Array.Copy(this[i].Array, this[i].Offset, fill2Data, fill2BuffOffset + offset, readL);
                offset += readL;
            }
        }
        /// <summary>
        /// 数据取出后就从当前列表中移除
        /// </summary>
        /// <param name="len"></param>
        /// <returns></returns>
        public byte[] getBytes(int len) {
            if (available() < len) return null;
            if (Count < 1) return null;
            byte[] data = new byte[len];
            int offset = 0;
            while (offset < data.Length) {
                ArraySegment<byte> seg = this[0];
                if (offset + seg.Count > len) {
                    Buffer.BlockCopy(seg.Array, seg.Offset, data, offset, len - offset);
                    updateOffset(seg.Offset + (len - offset));
                    return data;
                }
                else {
                    Buffer.BlockCopy(seg.Array, seg.Offset, data, offset, seg.Count);
                    offset += seg.Count;
                    this.RemoveAt(0);
                    //回收seg( 如果是规范内的buf长度 128b/256b/512b/1024b/...)  
                    //如果不是通过
                    ByteListPoolMgr.FreeBuf(seg.Array);
                }
            }
            // UnityEngine.Debug.Log("EXIT LOOP????!??!?!?!?? ");
            return data;
        }
        /// <summary>
        /// 数据取出后就从当前列表中移除
        /// </summary>
        /// <param name="fill2Data">填充数组，外面保证其剩余长度有效性</param>
        /// <param name="fill2BuffOffset"></param>
        /// <param name="len">外面保证取长度的有效性</param>
        public void getBytes(ref byte[] fill2Data, int fill2BuffOffset, int len) {
            if (available() < len) return;
            if (fill2Data.Length - fill2BuffOffset < len) return;
            if (Count < 1) return;
            //填充到数组
            int offset = 0;
            while (offset < len) {
                ArraySegment<byte> seg = this[0];
                if (offset + seg.Count > len) {
                    Buffer.BlockCopy(seg.Array, seg.Offset, fill2Data, offset + fill2BuffOffset, len - offset);
                    updateOffset(seg.Offset + (len - offset));
                    break;
                }
                else {
                    Buffer.BlockCopy(seg.Array, seg.Offset, fill2Data, offset + fill2BuffOffset, seg.Count);
                    offset += seg.Count;
                    this.RemoveAt(0);
                    //回收seg 如果不是通过ByteListPoolMgr申请的byte[] 并且长度不是64偶数倍（最大64）的数据将直接丢弃，由GC回收
                    ByteListPoolMgr.FreeBuf(seg.Array);
                }
            }

        }
        /// <summary>
        /// 数据取出后就从当前列表中移除
        /// </summary>
        /// <param name="len"></param>
        /// <returns></returns>
        public ByteListStream getBytesTo(int len) {
            if (available() < len) return null;
            if (Count < 1) return null;
            ByteListStream bs = new ByteListStream();
            int offset = 0;
            while (offset < len) {
                ArraySegment<byte> seg = this[0];
                if (offset + seg.Count > len) {
                    var level = ByteListPoolMgr.CaculBufLevel(seg.Count);
                    byte[] buf = ByteListPoolMgr.AllocBuf(level);
                    Buffer.BlockCopy(seg.Array, seg.Offset, buf, 0, len - offset);
                    updateOffset(seg.Offset + (len - offset));
                    //
                    bs.Add(new ArraySegment<byte>(buf, 0, len - offset));
                    break;
                }
                else {
                    //Buffer.BlockCopy(seg.Array, seg.Offset, buf, 0, seg.Count);
                    offset += seg.Count;
                    this.RemoveAt(0);
                    //回收seg 如果不是通过ByteListPoolMgr申请的byte[] 并且长度不是64偶数倍（最大64）的数据将直接丢弃，由GC回收
                    //ByteListPoolMgr.FreeBuf(seg.Array);
                    bs.Add(seg);
                }
            }
            return bs;
        }

        public int available() {
            int total = 0;
            foreach (ArraySegment<byte> seg in this) {
                total += seg.Count;
            }
            return total;
        }

        protected void updateOffset(int offset) {
            //更新第一个 offset与count
            ArraySegment<byte> seg = this[0];
            //this.RemoveAt(0);
            //ArraySegment<byte> item = new ArraySegment<byte>(seg.Array, offset, seg.Count - (offset - seg.Offset));
            //this.Insert(0, item);
            this[0]=new ArraySegment<byte>(seg.Array, offset, seg.Count - (offset - seg.Offset));
        }
        /// <summary>
        /// ArraySegmen.array最大长度为64K,超过64K报错
        /// </summary>
        /// <returns></returns>
        public ByteListStream Copy() {
            ByteListStream bs = new ByteListStream();
            for (int i = 0; i < this.Count; i++) {
                var level = ByteListPoolMgr.CaculBufLevel(this[i].Count);
                if (level == ByteListPool.BufferLevel.None) throw new OutOfMemoryException("ArraySegmen.array最大长度超过64K");
                byte[] buf = ByteListPoolMgr.AllocBuf(level);
                Buffer.BlockCopy(this[i].Array, this[i].Offset, buf, 0, this[i].Count);
                bs.Add(new ArraySegment<byte>(buf, 0, this[i].Count));
            }
            return bs;
        }
        /// <summary>
        /// 非copy模式 添加完之后src内容清空
        /// copy模式 添加完之后src内容不变
        /// </summary>
        /// <param name="src"></param>
        /// <param name="copySrc"></param>
        public void Append(ByteListStream src, bool copySrc = false) {
            if (copySrc) {
                var copy = src.Copy();
                this.Append(copy, false);
            }
            else {
                this.AddRange(src);
                src._clearItmes();
            }
        }
        protected void _clearItmes() {
            base.Clear();
        }
        [Obsolete("请使用Dispose()方法")]
        public new void Clear() {
            throw new Exception("请使用Dispose()方法");
        }
        #region dispose
        protected bool m_IsDispose;
        ~ByteListStream() {
            Dispose(false);
        }
        /// <summary>
        /// 回收所有关联array
        /// </summary>
        public void Dispose() {
            Dispose(true);
            //通知垃圾回收机制不再调用终结器（析构器）
            GC.SuppressFinalize(this);
        }
        protected void Dispose(bool Disposing) {
            if (m_IsDispose) {
                return;
            }
            m_IsDispose = true;
            if (Disposing) {
                //主动释放，则回收
                for (int i = this.Count - 1; i >= 0; i--) {
                    ByteListPoolMgr.FreeBuf(this[i].Array);
                }
                base.Clear();
            }
        }
        #endregion
    }
    public static class ByteListPoolMgr {
        static ByteListPool BufPool = new ByteListPool();
        static object m_lockObj = new object();
        /// <summary>
        /// 线程安全
        /// </summary>
        /// <param name="level"></param>
        /// <returns></returns>
        public static byte[] AllocBuf(ByteListPool.BufferLevel level) {
            lock (m_lockObj) {
                return BufPool.GetBuf(level);
            }
        }
        /// <summary>
        /// 线程安全
        /// </summary>
        /// <param name="buf"></param>
        public static void FreeBuf(byte[] buf) {
            lock (m_lockObj) {
                BufPool.Free(buf);
            }
        }
        public static void ClearBuf() {
            lock (m_lockObj) {
                BufPool.Clear();
            }
        }
        ///// <summary>
        ///// 默认缓存容量是80M，设置缓存容量倍数 设置为2则缓存容量是160M
        ///// </summary>
        ///// <param name="multiple">倍数必须大于0</param>
        //public static void SetCacheMultipleAll(byte multiple) {
        //    BufPool.SetCacheMultipleAll(multiple);
        //}
        /// <summary>
        /// 设置指定缓存容量的 缓存倍数,如果编写程序中命中的缓存级别较大，可单独增加缓存数量
        /// </summary>
        /// <param name="level"></param>
        /// <param name="multiple"></param>
        public static void SetCacheMultiple(ByteListPool.BufferLevel level, byte multiple) {
            BufPool.SetCacheMultiple(level, multiple);
        }
        public static ByteListPool.BufferLevel CaculBufLevel(int bytesSize) {
            if (bytesSize <= 128) return ByteListPool.BufferLevel.b128;
            else if (bytesSize <= 256) return ByteListPool.BufferLevel.b256;
            else if (bytesSize <= 512) return ByteListPool.BufferLevel.b512;
            else if (bytesSize <= 1024) return ByteListPool.BufferLevel.k1;
            else if (bytesSize <= 1024 * 2) return ByteListPool.BufferLevel.k2;
            else if (bytesSize <= 1024 * 4) return ByteListPool.BufferLevel.k4;
            else if (bytesSize <= 1024 * 8) return ByteListPool.BufferLevel.k8;
            else if (bytesSize <= 1024 * 16) return ByteListPool.BufferLevel.k16;
            else if (bytesSize <= 1024 * 32) return ByteListPool.BufferLevel.k32;
            else if (bytesSize <= 1024 * 64) return ByteListPool.BufferLevel.k64;
            return ByteListPool.BufferLevel.None;
        }



        /// <summary>
        /// 把大字节数组分割成指定容量等级的ByteListStream
        /// </summary>
        /// <param name="largeBuf"></param>
        /// <param name="level"></param>
        /// <returns></returns>
        public static ByteListStream Slice(byte[] largeBuf, ByteListPool.BufferLevel level) {
            ByteListStream bs = new ByteListStream();
            int countleave = largeBuf.Length;
            int offset = 0;
            while (countleave > 0) {
                byte[] buf = AllocBuf(level);
                int len = Math.Min(countleave, buf.Length);
                Buffer.BlockCopy(largeBuf, offset, buf, 0, len);
                bs.Add(new ArraySegment<byte>(buf, 0, len));
                offset += len;
                countleave -= len;
            }
            return bs;
        }

    }
    /// <summary>
    /// 连续申请内存对象(内存块比较多，GC引用数量大)
    /// </summary>
    public class ByteListPool {
        public enum BufferLevel {
            None,
            b128,
            b256,
            b512,
            k1,
            k2,
            k4,
            k8,
            k16,
            k32,
            k64
        }
        Queue<byte[]> Buffer128b = new Queue<byte[]>();
        Queue<byte[]> Buffer256b = new Queue<byte[]>();
        Queue<byte[]> Buffer512b = new Queue<byte[]>();
        Queue<byte[]> Buffer1k = new Queue<byte[]>();
        Queue<byte[]> Buffer2k = new Queue<byte[]>();
        Queue<byte[]> Buffer4k = new Queue<byte[]>();
        Queue<byte[]> Buffer8k = new Queue<byte[]>();
        Queue<byte[]> Buffer16k = new Queue<byte[]>();
        Queue<byte[]> Buffer32k = new Queue<byte[]>();
        Queue<byte[]> Buffer64k = new Queue<byte[]>();
        /// <summary>
        /// 缓存扩增倍数,比如初始是缓存80M，如果取2则是 80X2=160M
        /// </summary>
        private int m_cacheMultiple = 1;
        Dictionary<BufferLevel, int> m_cacheMultiples = new Dictionary<BufferLevel, int>();
        /// <summary>
        /// 默认缓存容量是80M，设置所有缓存容量倍数 设置为2则缓存容量是160M
        /// </summary>
        /// <param name="multiple">倍数必须大于0</param>
        public void SetCacheMultipleAll(byte multiple) {
            m_cacheMultiple = Math.Max(1, (int)multiple);
            m_cacheMultiples.Add(BufferLevel.None, m_cacheMultiple);
            m_cacheMultiples.Add(BufferLevel.b128, m_cacheMultiple);
            m_cacheMultiples.Add(BufferLevel.b256, m_cacheMultiple);
            m_cacheMultiples.Add(BufferLevel.b512, m_cacheMultiple);
            m_cacheMultiples.Add(BufferLevel.k1, m_cacheMultiple);
            m_cacheMultiples.Add(BufferLevel.k2, m_cacheMultiple);
            m_cacheMultiples.Add(BufferLevel.k4, m_cacheMultiple);
            m_cacheMultiples.Add(BufferLevel.k8, m_cacheMultiple);
            m_cacheMultiples.Add(BufferLevel.k16, m_cacheMultiple);
            m_cacheMultiples.Add(BufferLevel.k32, m_cacheMultiple);
            m_cacheMultiples.Add(BufferLevel.k64, m_cacheMultiple);
        }
        /// <summary>
        /// 设置指定缓存容量的 缓存倍数,如果编写程序中命中的缓存级别较大，可单独增加缓存数量
        /// </summary>
        /// <param name="level"></param>
        /// <param name="multiple"></param>
        public void SetCacheMultiple(BufferLevel level, byte multiple) {
            m_cacheMultiples[level] = Math.Max(1, (int)multiple);
        }
        /// <summary>
        /// 填充初始容量
        /// </summary>
        private void _Init() {
#if DEBUG
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
#endif

            for (int j = 0; j < 4; j++) {
                //初始申请20M缓存数据
                for (int i = 0; i < 4096; i++) {
                    //系统会尽量申请地址连续的内存（512K）唯一缺点内存碎片比较多
                    Buffer128b.Enqueue(new byte[128]);
                }
                for (int i = 0; i < 2048; i++) {
                    //系统会尽量申请地址连续的内存（512K）
                    Buffer256b.Enqueue(new byte[256]);
                }
                for (int i = 0; i < 1024; i++) {
                    //系统会尽量申请地址连续的内存（512K）
                    Buffer512b.Enqueue(new byte[512]);
                }
                for (int i = 0; i < 512; i++) {
                    //系统会尽量申请地址连续的内存（512K）
                    Buffer1k.Enqueue(new byte[1024]);
                }
                for (int i = 0; i < 256; i++) {
                    //系统会尽量申请地址连续的内存（512K）
                    Buffer2k.Enqueue(new byte[1024 * 2]);
                }
                for (int i = 0; i < 128; i++) {
                    //系统会尽量申请地址连续的内存（512K）
                    Buffer4k.Enqueue(new byte[1024 * 4]);
                }
                for (int i = 0; i < 64; i++) {
                    //系统会尽量申请地址连续的内存（512K）
                    Buffer8k.Enqueue(new byte[1024 * 8]);
                }
                for (int i = 0; i < 32; i++) {
                    //系统会尽量申请地址连续的内存（512K）
                    Buffer16k.Enqueue(new byte[1024 * 16]);
                }
                for (int i = 0; i < 16; i++) {
                    //系统会尽量申请地址连续的内存（512K）
                    Buffer32k.Enqueue(new byte[1024 * 32]);
                }
                for (int i = 0; i < 8; i++) {
                    //系统会尽量申请地址连续的内存（512K）
                    Buffer64k.Enqueue(new byte[1024 * 64]);
                }
            }
#if DEBUG
            sw.Stop();
            //UnityEngine.Debug.LogWarning("耗时(毫秒):" + sw.ElapsedMilliseconds);
#endif
        }
        public ByteListPool() {           
            //解决内存申请碎片化
            _Init();
            SetCacheMultipleAll(1);
        }


        /// <summary>
        /// 添加一次是512K数据
        /// </summary>
        /// <param name="level"></param>
        private void _addBuf(BufferLevel level) {
#if DEBUG
            //UnityEngine.Debug.LogWarning("##ByteListPool##_addBuf:" + level.ToString());
#endif
            switch (level) {
                case BufferLevel.b128:
                    for (int i = 0; i < 4096; i++) {
                        //系统会尽量申请地址连续的内存（512K）
                        Buffer128b.Enqueue(new byte[128]);
                    }
                    break;
                case BufferLevel.b256:
                    for (int i = 0; i < 2048; i++) {
                        Buffer256b.Enqueue(new byte[256]);
                    }
                    break;
                case BufferLevel.b512:
                    for (int i = 0; i < 1024; i++) {
                        Buffer512b.Enqueue(new byte[512]);
                    }
                    break;
                case BufferLevel.k1:
                    for (int i = 0; i < 512; i++) {
                        Buffer1k.Enqueue(new byte[1024]);
                    }
                    break;
                case BufferLevel.k2:
                    for (int i = 0; i < 256; i++) {
                        Buffer2k.Enqueue(new byte[1024 * 2]);
                    }
                    break;
                case BufferLevel.k4:
                    for (int i = 0; i < 128; i++) {
                        Buffer4k.Enqueue(new byte[1024 * 4]);
                    }
                    break;
                case BufferLevel.k8:
                    for (int i = 0; i < 64; i++) {
                        Buffer8k.Enqueue(new byte[1024 * 8]);
                    }
                    break;
                case BufferLevel.k16:
                    for (int i = 0; i < 32; i++) {
                        Buffer16k.Enqueue(new byte[1024 * 16]);
                    }
                    break;
                case BufferLevel.k32:
                    for (int i = 0; i < 16; i++) {
                        Buffer32k.Enqueue(new byte[1024 * 32]);
                    }
                    break;
                case BufferLevel.k64:
                    for (int i = 0; i < 8; i++) {
                        Buffer64k.Enqueue(new byte[1024 * 64]);
                    }
                    break;
                    //
            }
            //

        }
        public byte[] GetBuf(BufferLevel level) {
            byte[] buf = null;
            switch (level) {
                case BufferLevel.b128:
                    if (Buffer128b.Count == 0) {
                        _addBuf(level);
                    }
                    buf = this.Buffer128b.Dequeue();
                    //
                    break;
                case BufferLevel.b256:
                    if (Buffer256b.Count == 0) {
                        _addBuf(level);

                    }
                    buf = this.Buffer256b.Dequeue();
                    //
                    break;
                case BufferLevel.b512:
                    if (Buffer512b.Count == 0) {
                        _addBuf(level);
                    }
                    buf = this.Buffer512b.Dequeue();
                    //
                    break;
                case BufferLevel.k1:
                    if (Buffer1k.Count == 0) {
                        _addBuf(level);
                    }
                    buf = this.Buffer1k.Dequeue();
                    //
                    break;
                case BufferLevel.k2:
                    if (Buffer2k.Count == 0) {
                        _addBuf(level);
                    }
                    buf = this.Buffer2k.Dequeue();
                    //
                    break;
                case BufferLevel.k4:
                    if (Buffer4k.Count == 0) {
                        _addBuf(level);
                    }
                    buf = this.Buffer4k.Dequeue();
                    //
                    break;
                case BufferLevel.k8:
                    if (Buffer8k.Count == 0) {
                        _addBuf(level);
                    }
                    buf = this.Buffer8k.Dequeue();
                    //
                    break;
                case BufferLevel.k16:
                    if (Buffer16k.Count == 0) {
                        _addBuf(level);
                    }
                    buf = this.Buffer16k.Dequeue();
                    //
                    break;
                case BufferLevel.k32:
                    if (Buffer32k.Count == 0) {
                        _addBuf(level);
                    }
                    buf = this.Buffer32k.Dequeue();
                    //
                    break;
                case BufferLevel.k64:
                    if (Buffer64k.Count == 0) {
                        _addBuf(level);
                    }
                    buf = this.Buffer64k.Dequeue();
                    //
                    break;
                    //

            }
            return buf;

        }

        private void _recyle(byte[] buf, BufferLevel level) {
            //缓存限制数量，防止内存无止境增涨
            Array.Clear(buf, 0, buf.Length);
            //设置最大缓存80M数据
            switch (level) {
                case BufferLevel.b128:
                    if (Buffer128b.Count > 1024 * 64 * m_cacheMultiple) {
                        //超出数量范围(8M) 直接GC
#if DEBUG
                        //UnityEngine.Debug.LogWarning("##ByteListPool##_recyle:" + level.ToString());
#endif
                        break;
                    }
                    Buffer128b.Enqueue(buf);
                    break;
                case BufferLevel.b256:
                    if (Buffer256b.Count > 1024 * 32 * m_cacheMultiple) {
                        //超出数量范围(8M) 直接GC
#if DEBUG
                        //UnityEngine.Debug.LogWarning("##ByteListPool##_recyle:" + level.ToString());
#endif
                        break;
                    }
                    Buffer256b.Enqueue(buf);
                    break;
                case BufferLevel.b512:
                    if (Buffer512b.Count > 1024 * 16 * m_cacheMultiple) {
                        //超出数量范围(8M) 直接GC
#if DEBUG
                        //UnityEngine.Debug.LogWarning("##ByteListPool##_recyle:" + level.ToString());
#endif
                        break;
                    }
                    Buffer512b.Enqueue(buf);
                    break;
                case BufferLevel.k1:
                    if (Buffer1k.Count > 1024 * 8 * m_cacheMultiple) {
                        //超出数量范围(8m) 直接GC
#if DEBUG
                        //UnityEngine.Debug.LogWarning("##ByteListPool##_recyle:" + level.ToString());
#endif
                        break;
                    }
                    Buffer1k.Enqueue(buf);
                    break;
                case BufferLevel.k2:
                    if (Buffer2k.Count > 1024 * 4 * m_cacheMultiple) {
                        //超出数量范围(8m) 直接GC
#if DEBUG
                        //UnityEngine.Debug.LogWarning("##ByteListPool##_recyle:" + level.ToString());
#endif
                        break;
                    }
                    Buffer2k.Enqueue(buf);
                    break;
                case BufferLevel.k4:
                    if (Buffer4k.Count > 1024 * 2 * m_cacheMultiple) {
                        //超出数量范围(8m) 直接GC
#if DEBUG
                        //UnityEngine.Debug.LogWarning("##ByteListPool##_recyle:" + level.ToString());
#endif
                        break;
                    }
                    Buffer4k.Enqueue(buf);
                    break;
                case BufferLevel.k8:
                    if (Buffer8k.Count > 1024 * m_cacheMultiple) {
                        //超出数量范围(8m) 直接GC
#if DEBUG
                        //UnityEngine.Debug.LogWarning("##ByteListPool##_recyle:" + level.ToString());
#endif
                        break;
                    }
                    Buffer8k.Enqueue(buf);
                    break;
                case BufferLevel.k16:
                    if (Buffer16k.Count > 512 * m_cacheMultiple) {
                        //超出数量范围(8m) 直接GC
#if DEBUG
                        //UnityEngine.Debug.LogWarning("##ByteListPool##_recyle:" + level.ToString());
#endif
                        break;
                    }
                    Buffer16k.Enqueue(buf);
                    break;
                case BufferLevel.k32:
                    if (Buffer32k.Count > 256 * m_cacheMultiple) {
                        //超出数量范围(8m) 直接GC
#if DEBUG
                        //UnityEngine.Debug.LogWarning("##ByteListPool##_recyle:" + level.ToString());
#endif
                        break;
                    }
                    Buffer32k.Enqueue(buf);
                    break;
                case BufferLevel.k64:
                    if (Buffer64k.Count > 128 * m_cacheMultiple) {
                        //超出数量范围(8m) 直接GC
#if DEBUG
                        //UnityEngine.Debug.LogWarning("##ByteListPool##_recyle:" + level.ToString());
#endif
                        break;
                    }
                    Buffer64k.Enqueue(buf);
                    break;
                    //
            }
        }

        public void Free(byte[] buf) {
            BufferLevel level = BufferLevel.None;
            switch (buf.Length) {
                case 128:
                    level = BufferLevel.b128;
                    break;
                case 256:
                    level = BufferLevel.b256;
                    break;
                case 512:
                    level = BufferLevel.b512;
                    break;
                case 1024:
                    level = BufferLevel.k1;
                    break;
                case 2048:
                    level = BufferLevel.k2;
                    break;
                case 4096:
                    level = BufferLevel.k4;
                    break;
                case 8192:
                    level = BufferLevel.k8;
                    break;
                case 16384:
                    level = BufferLevel.k16;
                    break;
                case 32768:
                    level = BufferLevel.k32;
                    break;
                case 65536:
                    level = BufferLevel.k64;
                    break;
            }
            //不是规范内的buf不回收，直接丢弃
            _recyle(buf, level);
        }

        public void Clear() {
            this.Buffer128b.Clear();
            this.Buffer256b.Clear();
            this.Buffer512b.Clear();
            this.Buffer1k.Clear();
            this.Buffer2k.Clear();
            this.Buffer4k.Clear();
            this.Buffer8k.Clear();
            this.Buffer16k.Clear();
            this.Buffer32k.Clear();
            this.Buffer64k.Clear();
        }
    }

   

}
