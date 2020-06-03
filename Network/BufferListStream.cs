using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace uNet2.Network {

    /// <summary>
    /// 提供一个可变 指定 N长度的数组组合 存放byte[]数据内容对象
    /// 用于申请连续内存数据，阻止byte[]碎片化
    /// </summary>
    public class BufferListStream : IDisposable {
        static void test() {
            //从缓存中取buf 用完回收 最大支持64K缓存数据
            Random rn = new Random();
            BufferListStream bs = new BufferListStream();
            for (int i = 0; i < 4; i++) {
                //从缓存池中申请byte数组
                var v128 = BufferListPoolMgr.AllocBuf(BufferListPool.BufferLevel.b128);
                int offset = v128.Offset + 2;
                v128.SetLength(offset, 100);
                for (int j = 0; j < 100; j++) {
                    v128.Array[offset + j] = (byte)(j + 1);
                }
                //大数据分片放入BufferListStream
                bs.Add(v128);
                
            }
            //读取数据
            byte[] buf = bs.getBytes(128);
            byte[] buf2 = new byte[64];
            //覆盖读取数据
            bs.getBytes(ref buf2, 0, buf2.Length);
            //销毁并回收关联的内存
            bs.Dispose();

        }

        List<BufferListSegment> m_List = new List<BufferListSegment>();
        public BufferListSegment this[int index] {
            get {
                return m_List[index];
            }
            set {
                throw new NotSupportedException();
            }
        }
        //public IList<BufferListSegment> GetAllCachedItems() {
        //    return m_List;
        //}
        //public IEnumerator<BufferListSegment> GetEnumerator() {
        //    int length = m_List.Count;
        //    for (int i = 0; i < length; i++) {
        //        yield return m_List[i];
        //    }
        //}
        //IEnumerator IEnumerable.GetEnumerator() {
        //    return GetEnumerator();
        //}
        public void Add(BufferListSegment item) {
            m_List.Add(item);
        }
        public int Count {
            get { return m_List.Count; }
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
                int readL = (leave > this.m_List[i].Count) ? this.m_List[i].Count : leave;
                Array.Copy(this.m_List[i].Array, this.m_List[i].Offset, data, offset, readL);
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
                int readL = (leave > this.m_List[i].Count) ? this.m_List[i].Count : leave;
                Array.Copy(this.m_List[i].Array, this.m_List[i].Offset, fill2Data, fill2BuffOffset + offset, readL);
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
                BufferListSegment seg = this.m_List[0];
                if (offset + seg.Count > len) {
                    Buffer.BlockCopy(seg.Array, seg.Offset, data, offset, len - offset);
                    updateOffset(seg.Offset + (len - offset));
                    return data;
                }
                else {
                    Buffer.BlockCopy(seg.Array, seg.Offset, data, offset, seg.Count);
                    offset += seg.Count;
                    this.m_List.RemoveAt(0);
                    //回收seg( 如果是规范内的buf长度 128b/256b/512b/1024b/...)  
                    //如果不是通过
                    BufferListPoolMgr.FreeBuf(seg);
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
                BufferListSegment seg = this.m_List[0];
                if (offset + seg.Count > len) {
                    Buffer.BlockCopy(seg.Array, seg.Offset, fill2Data, offset + fill2BuffOffset, len - offset);
                    updateOffset(seg.Offset + (len - offset));
                    break;
                }
                else {
                    Buffer.BlockCopy(seg.Array, seg.Offset, fill2Data, offset + fill2BuffOffset, seg.Count);
                    offset += seg.Count;
                    this.m_List.RemoveAt(0);
                    //回收seg 如果不是通过ByteListPoolMgr申请的byte[] 并且长度不是64偶数倍（最大64）的数据将直接丢弃，由GC回收
                    BufferListPoolMgr.FreeBuf(seg);
                }
            }

        }
        /// <summary>
        /// 数据取出后就从当前列表中移除
        /// </summary>
        /// <param name="len"></param>
        /// <returns></returns>
        public BufferListStream getBytesTo(int len) {
            if (available() < len) return null;
            if (Count < 1) return null;
            BufferListStream bs = new BufferListStream();
            int offset = 0;
            while (offset < len) {
                BufferListSegment seg = this.m_List[0];
                if (offset + seg.Count > len) {
                    var level = BufferListPoolMgr.CaculBufLevel(seg.Count);
                    var buf = BufferListPoolMgr.AllocBuf(level);
                    //Buffer.BlockCopy(seg.Array, seg.Offset, buf.Array, buf.Offset, len - offset);
                    buf.SetDataFast(seg.Array, seg.Offset, buf.Offset, len - offset);
                    updateOffset(seg.Offset + (len - offset));
                    //
                    bs.Add(buf);
                    break;
                }
                else {
                    //Buffer.BlockCopy(seg.Array, seg.Offset, buf, 0, seg.Count);
                    offset += seg.Count;
                    this.m_List.RemoveAt(0);
                    //回收seg 如果不是通过BufferListPoolMgr申请的byte[] 并且长度不是64偶数倍（最大64）的数据将直接丢弃，由GC回收
                    //BufferListPoolMgr.FreeBuf(seg.Array);
                    bs.Add(seg);
                }
            }
            return bs;
        }

        public int available() {
            int total = 0;
            foreach (var seg in this.m_List) {
                total += seg.Count;
            }
            return total;
        }

        protected void updateOffset(int offset) {
            //更新第一个 offset与count
            var seg = this.m_List[0];
            //this.RemoveAt(0);
            ////
            //int len = seg.Count - (offset - seg.Offset);
            //var level = BufferListPoolMgr.CaculBufLevel(len);
            //var item = BufferListPoolMgr.AllocBuf(level);
            ////Array.Copy(seg.Source.Array, offset, item.Source.Array, item.Source.Offset, len);
            //item.SetData(seg.Array,offset,len);                
            //this.Insert(0, item);
            //BufferListPoolMgr.FreeBuf(seg);
            int len = seg.Count - (offset - seg.Offset);
            seg.SetLength(offset, len);
        }
        /// <summary>
        /// ArraySegmen.array最大长度为64K,超过64K报错
        /// </summary>
        /// <returns></returns>
        public BufferListStream Copy() {
            BufferListStream bs = new BufferListStream();
            for (int i = 0; i < this.Count; i++) {
                var level = BufferListPoolMgr.CaculBufLevel(this.m_List[i].Count);
                if (level == BufferListPool.BufferLevel.None) throw new OutOfMemoryException("ArraySegmen.array最大长度超过64K");
                var buf = BufferListPoolMgr.AllocBuf(level);
                //Buffer.BlockCopy(this[i].Source.Array, this[i].Source.Offset, buf.Source.Array, buf.Source.Offset, this[i].Source.Count);                
                buf.SetDataFast(this.m_List[i].Array, this.m_List[i].Offset, buf.Offset, this.m_List[i].Count);
                bs.Add(buf);
            }
            return bs;
        }
        /// <summary>
        /// 非copy模式 添加完之后src内容清空
        /// copy模式 添加完之后src内容不变
        /// </summary>
        /// <param name="src"></param>
        /// <param name="copySrc"></param>
        public void Append(BufferListStream src, bool copySrc = false) {
            if (copySrc) {
                var copy = src.Copy();
                this.Append(copy, false);
            }
            else {
                this.m_List.AddRange(src.m_List);
                src._clearItmes();
            }
        }
        protected void _clearItmes() {
            this.m_List.Clear();
        }
        //[Obsolete("请使用Dispose()方法")]
        //public new void Clear() {
        //    throw new Exception("请使用Dispose()方法");
        //}
        #region dispose
        protected bool m_IsDispose;
        ~BufferListStream() {
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
            //if (Disposing) {
            //主动释放，则回收
            for (int i = this.Count - 1; i >= 0; i--) {
                BufferListPoolMgr.FreeBuf(this.m_List[i]);
            }
            this.m_List.Clear();
            //}
        }


        #endregion
    }
    public static class BufferListPoolMgr {
        static BufferListPool BufPool = new BufferListPool();
        //static object m_lockObj = new object();
        /// <summary>
        /// 线程安全
        /// </summary>
        /// <param name="level"></param>
        /// <returns></returns>
        public static BufferListSegment AllocBuf(BufferListPool.BufferLevel level) {
            //lock (m_lockObj) {
            return BufPool.GetBuf(level);
            //}
        }
        /// <summary>
        /// 线程安全
        /// </summary>
        /// <param name="buf"></param>
        public static void FreeBuf(BufferListSegment buf) {
            //lock (m_lockObj) {
            BufPool.Free(buf);
            //}
        }
        //[Obsolete("就不应该有清理")]
        //internal static void ClearBuf() {
        //    //lock (m_lockObj) {
        //    BufPool.Clear();
        //    //}
        //}
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
        public static void SetCacheMultiple(BufferListPool.BufferLevel level, byte multiple) {
            BufPool.SetCacheMultiple(level, multiple);
        }
        public static BufferListPool.BufferLevel CaculBufLevel(int bytesSize) {
            if (bytesSize <= 128) return BufferListPool.BufferLevel.b128;
            else if (bytesSize <= 256) return BufferListPool.BufferLevel.b256;
            else if (bytesSize <= 512) return BufferListPool.BufferLevel.b512;
            else if (bytesSize <= 1024) return BufferListPool.BufferLevel.k1;
            else if (bytesSize <= 1024 * 2) return BufferListPool.BufferLevel.k2;
            else if (bytesSize <= 1024 * 4) return BufferListPool.BufferLevel.k4;
            else if (bytesSize <= 1024 * 8) return BufferListPool.BufferLevel.k8;
            else if (bytesSize <= 1024 * 16) return BufferListPool.BufferLevel.k16;
            else if (bytesSize <= 1024 * 32) return BufferListPool.BufferLevel.k32;
            else if (bytesSize <= 1024 * 64) return BufferListPool.BufferLevel.k64;
            return BufferListPool.BufferLevel.None;
        }



        /// <summary>
        /// 把大字节数组分割成指定容量等级的ByteListStream
        /// </summary>
        /// <param name="largeBuf"></param>
        /// <param name="level"></param>
        /// <returns></returns>
        public static BufferListStream Slice(byte[] largeBuf, BufferListPool.BufferLevel level) {
            BufferListStream bs = new BufferListStream();
            int countleave = largeBuf.Length;
            int offset = 0;
            while (countleave > 0) {
                var buf = AllocBuf(level);
                int len = Math.Min(countleave, buf.Count);
                //Buffer.BlockCopy(largeBuf, offset, buf.Source.Array, buf.Source.Offset, len);
                buf.SetData(largeBuf, offset, len);
                bs.Add(buf);
                offset += len;
                countleave -= len;
            }
            return bs;
        }

    }
    /// <summary>
    /// 申请连续的内存缓存(内存块就按不同长度的几个)
    /// </summary>
    public class BufferListPool {
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
        //默认缓存都为8M
        BufferPool Buffer128b = null;
        BufferPool Buffer256b = null;
        BufferPool Buffer512b = null;
        BufferPool Buffer1k = null;
        BufferPool Buffer2k = null;
        BufferPool Buffer4k = null;
        BufferPool Buffer8k = null;
        BufferPool Buffer16k = null;
        BufferPool Buffer32k = null;
        BufferPool Buffer64k = null;
        /// <summary>
        /// 默认总缓存容量是80M，设置所有缓存容量倍数 设置为2则总缓存容量是160M
        /// </summary>
        /// <param name="multiple">倍数必须大于0</param>
        public void SetCacheMultipleAll(byte multiple) {
            Buffer128b.SetPageSizeMaxMultiple(multiple);
            Buffer256b.SetPageSizeMaxMultiple(multiple);
            Buffer512b.SetPageSizeMaxMultiple(multiple);
            Buffer1k.SetPageSizeMaxMultiple(multiple);
            Buffer2k.SetPageSizeMaxMultiple(multiple);
            Buffer4k.SetPageSizeMaxMultiple(multiple);
            Buffer8k.SetPageSizeMaxMultiple(multiple);
            Buffer16k.SetPageSizeMaxMultiple(multiple);
            Buffer32k.SetPageSizeMaxMultiple(multiple);
            Buffer64k.SetPageSizeMaxMultiple(multiple);
        }
        /// <summary>
        /// 设置指定缓存容量的 缓存倍数,如果编写程序中命中的缓存级别较大，可单独增加缓存数量
        /// </summary>
        /// <param name="level"></param>
        /// <param name="multiple"></param>
        public void SetCacheMultiple(BufferLevel level, byte multiple) {
            switch (level) {
                case BufferLevel.b128:
                    Buffer128b.SetPageSizeMaxMultiple(multiple);
                    break;
                case BufferLevel.b256:
                    Buffer256b.SetPageSizeMaxMultiple(multiple);
                    break;
                case BufferLevel.b512:
                    Buffer512b.SetPageSizeMaxMultiple(multiple);
                    break;
                case BufferLevel.k1:
                    Buffer1k.SetPageSizeMaxMultiple(multiple);
                    break;
                case BufferLevel.k2:
                    Buffer2k.SetPageSizeMaxMultiple(multiple);
                    break;
                case BufferLevel.k4:
                    Buffer4k.SetPageSizeMaxMultiple(multiple);
                    break;
                case BufferLevel.k8:
                    Buffer4k.SetPageSizeMaxMultiple(multiple);
                    break;
                case BufferLevel.k16:
                    Buffer8k.SetPageSizeMaxMultiple(multiple);
                    break;
                case BufferLevel.k32:
                    Buffer16k.SetPageSizeMaxMultiple(multiple);
                    break;
                case BufferLevel.k64:
                    Buffer64k.SetPageSizeMaxMultiple(multiple);
                    break;
            }
        }
        /// <summary>
        /// 填充初始容量
        /// </summary>
        private void _Init() {
#if DEBUG
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
#endif
            //默认每个缓存区的最大缓存都是8M
            this.Buffer128b = new BufferPool(1024 * 128, 1024 * 1024 * 8, 128);
            this.Buffer256b = new BufferPool(1024 * 128, 1024 * 1024 * 8, 256);
            this.Buffer512b = new BufferPool(1024 * 128, 1024 * 1024 * 8, 512);
            this.Buffer1k = new BufferPool(1024 * 128, 1024 * 1024 * 8, 1024);
            this.Buffer2k = new BufferPool(1024 * 128, 1024 * 1024 * 8, 1024 * 2);
            this.Buffer4k = new BufferPool(1024 * 128, 1024 * 1024 * 8, 1024 * 4);
            this.Buffer8k = new BufferPool(1024 * 128, 1024 * 1024 * 8, 1024 * 8);
            this.Buffer16k = new BufferPool(1024 * 128, 1024 * 1024 * 8, 1024 * 16);
            this.Buffer32k = new BufferPool(1024 * 128, 1024 * 1024 * 8, 1024 * 32);
            this.Buffer64k = new BufferPool(1024 * 128, 1024 * 1024 * 8, 1024 * 64);

#if DEBUG
            sw.Stop();
            //UnityEngine.Debug.LogWarning("耗时(毫秒):" + sw.ElapsedMilliseconds);
#endif
        }
        public BufferListPool() {
            //解决内存申请碎片化
            _Init();
            SetCacheMultipleAll(1);
        }

        public BufferListSegment GetBuf(BufferLevel level) {
            BufferListSegment seg = null;
            ArraySegment<byte> buf = new ArraySegment<byte>();
            switch (level) {
                case BufferLevel.b128:
                    buf = this.Buffer128b.GetBuffer(128);
                    seg = new BufferListSegment(level, buf, this.Buffer128b);
                    //
                    break;
                case BufferLevel.b256:
                    buf = this.Buffer256b.GetBuffer(256);
                    seg = new BufferListSegment(level, buf, this.Buffer256b);
                    //
                    break;
                case BufferLevel.b512:
                    buf = this.Buffer512b.GetBuffer(512);
                    seg = new BufferListSegment(level, buf, this.Buffer512b);
                    //
                    break;
                case BufferLevel.k1:
                    buf = this.Buffer1k.GetBuffer(1024);
                    seg = new BufferListSegment(level, buf, this.Buffer1k);
                    //
                    break;
                case BufferLevel.k2:
                    buf = this.Buffer2k.GetBuffer(1024 * 2);
                    seg = new BufferListSegment(level, buf, this.Buffer2k);
                    //
                    break;
                case BufferLevel.k4:
                    buf = this.Buffer4k.GetBuffer(1024 * 4);
                    seg = new BufferListSegment(level, buf, this.Buffer4k);
                    //
                    break;
                case BufferLevel.k8:
                    buf = this.Buffer8k.GetBuffer(1024 * 8);
                    seg = new BufferListSegment(level, buf, this.Buffer8k);
                    //
                    break;
                case BufferLevel.k16:
                    buf = this.Buffer16k.GetBuffer(1024 * 16);
                    seg = new BufferListSegment(level, buf, this.Buffer16k);
                    //
                    break;
                case BufferLevel.k32:
                    buf = this.Buffer32k.GetBuffer(1024 * 32);
                    seg = new BufferListSegment(level, buf, this.Buffer32k);
                    //
                    break;
                case BufferLevel.k64:
                    buf = this.Buffer64k.GetBuffer(1024 * 64);
                    seg = new BufferListSegment(level, buf, this.Buffer64k);
                    //
                    break;
                    //

            }
            if (buf.Array == null) {
                //todo:超出缓存，取不出来
                switch (level) {
                    case BufferLevel.b128:
                        buf = new ArraySegment<byte>(new byte[128]);
                        //
                        break;
                    case BufferLevel.b256:
                        buf = new ArraySegment<byte>(new byte[256]);
                        //
                        break;
                    case BufferLevel.b512:
                        buf = new ArraySegment<byte>(new byte[512]);
                        //
                        break;
                    case BufferLevel.k1:
                        buf = new ArraySegment<byte>(new byte[1024]);
                        //
                        break;
                    case BufferLevel.k2:
                        buf = new ArraySegment<byte>(new byte[1024 * 2]);
                        //
                        break;
                    case BufferLevel.k4:
                        buf = new ArraySegment<byte>(new byte[1024 * 4]);
                        //
                        break;
                    case BufferLevel.k8:
                        buf = new ArraySegment<byte>(new byte[1024 * 8]);
                        //
                        break;
                    case BufferLevel.k16:
                        buf = new ArraySegment<byte>(new byte[1024 * 16]);
                        //
                        break;
                    case BufferLevel.k32:
                        buf = new ArraySegment<byte>(new byte[1024 * 32]);
                        //
                        break;
                    case BufferLevel.k64:
                        buf = new ArraySegment<byte>(new byte[1024 * 64]);
                        //
                        break;
                        //

                }
                seg = new BufferListSegment(level, buf, null);
            }
            return seg;

        }

        public void Free(BufferListSegment buf) {
            //缓存限制数量，防止内存无止境增涨
            //Array.Clear(buf.Source.Array, buf.Source.Offset, buf.Source.Count);
            //if (buf.IsFromBufferListPool == false) {
            //buf内部自己已经判断
            //    return;
            //}
            buf.ReleaseBuffer();
            //设置最大缓存80M数据
            //switch (buf.Level) {
            //    case BufferLevel.b128:
            //        //内部已经执行Array.Clear(...)
            //        buf.ReleaseBuffer(Buffer128b);
            //        break;
            //    case BufferLevel.b256:
            //        buf.ReleaseBuffer(Buffer256b);
            //        break;
            //    case BufferLevel.b512:
            //        buf.ReleaseBuffer(Buffer512b);
            //        break;
            //    case BufferLevel.k1:
            //        buf.ReleaseBuffer(Buffer1k);
            //        break;
            //    case BufferLevel.k2:
            //        buf.ReleaseBuffer(Buffer2k);
            //        break;
            //    case BufferLevel.k4:
            //        buf.ReleaseBuffer(Buffer4k);
            //        break;
            //    case BufferLevel.k8:
            //        buf.ReleaseBuffer(Buffer8k);
            //        break;
            //    case BufferLevel.k16:
            //        buf.ReleaseBuffer(Buffer16k);
            //        break;
            //    case BufferLevel.k32:
            //        buf.ReleaseBuffer(Buffer32k);
            //        break;
            //    case BufferLevel.k64:
            //        buf.ReleaseBuffer(Buffer64k);
            //        break;
            //        //
            //}
        }
        //[Obsolete("1:外面解决关联对象 2:在多线程情况下，执行改方法则会可能造成异常，外面保证其安全性")]
        //internal void Clear() {
        //    this.Buffer128b.Clear();
        //    this.Buffer256b.Clear();
        //    this.Buffer512b.Clear();
        //    this.Buffer1k.Clear();
        //    this.Buffer2k.Clear();
        //    this.Buffer4k.Clear();
        //    this.Buffer8k.Clear();
        //    this.Buffer16k.Clear();
        //    this.Buffer32k.Clear();
        //    this.Buffer64k.Clear();
        //}
    }
    /// <summary>
    /// BufferList片段
    /// </summary>
    public class BufferListSegment {

        public BufferListSegment(BufferListPool.BufferLevel level, ArraySegment<byte> source, BufferPool buffPool = null) {
            this.Level = level;
            this.m_OffsetOriginal = source.Offset;
            this.m_CountOriginal = source.Count;
            //
            this.Offset = source.Offset;
            this.Count = source.Count;
            //
            if (buffPool != null) {
                this.IsFromBufferListPool = true;
                this.m_BufferPool = buffPool;
            }
            else {
                this.m_Source = source;
            }
        }
        //
        private BufferPool m_BufferPool;
        private ArraySegment<byte> m_Source;
        private int m_OffsetOriginal = 0;
        private int m_CountOriginal = 0;
        //
        public BufferListPool.BufferLevel Level;
        public bool IsFromBufferListPool {
            get;
            private set;
        }
        /// <summary>
        /// 关联array数组,外部不要直接调用Buffer.BlockCopy(...) Array.Copy(...) 对当前数组赋值，请使用SetData(...)方法
        /// </summary>
        public byte[] @Array {
            get {
                if (this.IsFromBufferListPool) {
                    //因为BufferPool
                    return this.m_BufferPool.GetCurrentArray();
                }
                return this.m_Source.Array;
            }
        }
        /// <summary>
        /// 真正offset
        /// </summary>
        public int Offset { get; private set; }
        /// <summary>
        /// 真正数量
        /// </summary>
        public int Count { get; private set; }
        public void SetData(byte[] src, int srcoffset, int count) {
            SetData(src, srcoffset, this.Offset, count);
        }
        public void SetData(byte[] src, int srcoffset, int offset, int count) {
            //源与目标数组如果有重叠部分, 则使用Array.Copy(...)保证copy的正确性           
            System.Array.Copy(src, srcoffset, this.Array, offset, count);
            //this.Offset = offset;
            //this.Count = count;
            SetLength(offset, count);
        }
        /// <summary>
        /// 内部不做检测，外部绝对 保证源与目标数组没有重叠部分，并且offset位置正确
        /// </summary>
        /// <param name="src"></param>
        /// <param name="srcoffset"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        internal void SetDataFast(byte[] src, int srcoffset, int offset, int count) {
            //源与目标数组没有重叠部分
            Buffer.BlockCopy(src, srcoffset, this.Array, offset, count);
            this.Offset = offset;
            this.Count = count;
        }
        /// <summary>
        /// 设置数据长度
        /// </summary>
        /// <param name="count"></param>
        public void SetLength(int count) {
            this.Count = count;
        }
        /// <summary>
        /// 设置数据长度
        /// </summary>
        /// <param name="offset">真正offset</param>
        /// <param name="count">真正数量</param>
        public void SetLength(int offset, int count) {
            if (offset < this.m_OffsetOriginal) {
                throw new Exception("设置索引异常,数值不能比原始索引小.");
            }
            if (count > this.m_CountOriginal) {
                throw new Exception("设置长度异常,数值不能比原始长度大.");
            }
            this.Offset = offset;
            this.Count = count;
        }

        /// <summary>
        /// 由管理器调用
        /// </summary>
        /// <param name="bufferPool"></param>
        public void ReleaseBuffer() {
            if (this.IsFromBufferListPool) {
                this.m_BufferPool.ReleaseBuffer(new ArraySegment<byte>(this.Array, this.m_OffsetOriginal, this.m_CountOriginal));
            }

        }
    }
    /// <summary>
    /// A <see cref="BufferPool"/> 是为客户端提供可重用缓冲区的对象.能很好替代CircularBuffer
    /// </summary>
    /// <remarks>
    ///客户端可以从池中获取所需大小的缓冲区，并在完成后将其释放回池中
    /// 
    ///缓存区由该类管理的数组<see cref="ArraySegment{T}"/>支持的.
    /// 
    /// 当缓冲区释放回池中时，它将被清除，并且对缓冲区的引用将保留在空闲列表中.
    /// 这可以防止垃圾收集器收集缓冲区。下次客户机请求该大小的缓冲区时，以前释放的缓冲区将对客户机可用并从空闲列表中删除.
    /// 
    /// 仅为块大小的倍数的大小维护可用列表。底层数组的部分按照这些大小分配，称为段。
    /// 如果客户机请求映射到给定段大小的缓冲区，并且该段大小的空闲列表具有可用缓冲区，则该缓冲区将从空闲列表中删除，
    /// 并将其大小与请求的缓冲区大小进行比较。 如果这些大小相同，则将缓冲区返回给客户端。如果尺寸不同, 然后创建请求大小的新<see cref="ArraySegment{T}"/>
    /// 来代替旧的缓冲区. 它由数组的同一段支持，但缓冲区的分配确实发生了。
    /// 
    /// 这个类的性能特性可以由两个因素控制：页面大小和块大小。页面大小影响需要执行的数组大小调整操作的数量。
    /// 块大小影响需要维护多少可用列表。这两个因素都决定了“浪费”了多少空间，既包括底层缓冲区中未分配的空间，
    /// 也包括所服务的缓冲区中未使用的空间. 页面大小必须可以被块大小均匀地整除，并且必须是2的幂。
    /// 
    /// 块大小还影响池必须进行多少<see cref="ArraySegment{T}"/>分配。如果客户
    /// </remarks>
    public class BufferPool {
        /// <summary>
        /// [readonly]页面大小控制数组重新调整大小的行为。数组大小总是页面大小的倍数。
        /// The page size controls the array re-sizing behavior. Array sizes are always a multiple of the page size.
        /// </summary>
        /// <devdoc>必须为2的倍数Must be a power of 2.</devdoc>
        private readonly int pageSize;
        /// <summary>
        /// [readonly]最大容量 可扩容
        /// </summary>
        private readonly int pageSizeMax;
        /// <summary>
        /// 缓存扩增倍数,比如初始是缓存8M，如果取2则是 8X2=16M
        /// </summary>
        private int pageSizeMaxMultiple = 1;
        /// <summary>
        /// [readonly]可以创建的最小缓冲区大小
        /// The minimum buffer size that can be created.
        /// 必须平均划分页面大小
        /// </summary>
        /// <devdoc>Must evenly divide the page size.</devdoc>
        private readonly int blockSize;

        /// <summary>
        /// 支持所有缓冲区的原始字节数组
        /// The raw byte array that backs all of the buffers.
        /// </summary>
        private byte[] rawBytes;
        /// <summary>
        /// rawBytes一旦Array.ReSize(...)后 对象地址会发生变化,注意旧的引用更新问题
        /// </summary>
        /// <returns></returns>
        public byte[] GetCurrentArray() {
            return rawBytes;
        }
        /// <summary>
        /// An object used to lock on <see cref="rawBytes"/>.
        /// 注意：不能安全锁定，因为对它的引用可以通过数组段获得
        /// </summary>
        /// <devdoc>
        /// <see cref="rawBytes"/> isn't safe to lock on since a reference to it may be obtained via the array segments.
        /// </devdoc>
        private readonly object rawBytesLock = new object();

        /// <summary>
        /// 缓冲区中第一个可用元素的索引，它标记未分配空间的开头。
        /// The index of the first free element in the buffer, which marks the beginning of unallocated space.
        /// 值是递增的
        /// </summary>
        /// <devdoc>
        /// This value increases monotonically.
        /// </devdoc>
        private int freeIndex;

        /// <summary>
        /// 可用列表，按缓冲区大小索引.它们包含任何已经释放并准备好重用的"ArraySegment"对象。
        /// Free lists, indexed by buffer size. These contain any <see cref="ArraySegment{T}"/> objects that have been
        /// released and are ready for reuse.
        /// </summary>
        private Dictionary<int, Queue<ArraySegment<byte>>> freeLists = new Dictionary<int, Queue<ArraySegment<byte>>>();

        /// <summary>
        /// 空闲列表中任何缓冲区的大小。
        /// Sizes of any buffers in the free lists.
        /// 用于快速确定给定大小的缓冲区是否可用。
        /// </summary>
        /// <devdoc>Used to quickly determine if a buffer of a given size is available.</devdoc>
        private HashSet<int> freeSizes = new HashSet<int>();
        /// <summary>
        /// 默认缓存容量是8M，设置缓存容量倍数 设置为2则缓存容量是16M
        /// </summary>
        /// <param name="multiple">倍数必须大于0</param>
        public void SetPageSizeMaxMultiple(byte multiple) {
            pageSizeMaxMultiple = Math.Max(1, (int)multiple);
        }
        /// <summary>
        /// 使用给定的分配大小初始化缓冲池
        /// Initializes a buffer pool with the given allocation size.
        /// </summary>
        /// <param name="pageSize">
        /// 初始分配空间的字节大小。任何进一步的分配都将是这个值的倍数。一定是2的幂。
        /// The size in bytes for the initial allocated space. Any further allocations will be in multiples of this 
        /// value. Must be a power of 2.
        /// </param>
        /// <param name="blockSize">
        /// 块大小，它指示将向上舍入缓冲区的大小。一定是2的幂。
        /// The block size, which dictates the size that buffers will be rounded up to. Must be a power of 2.
        /// </param>
        public BufferPool(int pageSize, int pageSizeMax, int blockSize = 32) {
            if (blockSize == 0 || (blockSize & (blockSize - 1)) != 0) {
                throw new ArgumentException($"{nameof(blockSize)} must be a power of 2.");
            }
            if (pageSize < blockSize || (pageSize % blockSize) != 0) {
                throw new ArgumentException($"{nameof(pageSize)} must be divisible by {nameof(blockSize)}.");
            }
            if (pageSizeMax <= pageSize) {
                throw new ArgumentException($"{nameof(pageSizeMax)} must larger than {nameof(pageSize)}.");
            }
            this.pageSize = pageSize;
            this.pageSizeMax = pageSizeMax;
            this.blockSize = blockSize;
            rawBytes = new byte[pageSize];
            freeIndex = 0;
        }
        ///// <summary>
        /////不应该存在  清理(还原成初始状态)
        /////否则外面拿住的引用对象就可能产生异常
        ///// </summary>
        //[Obsolete("在多线程情况下，执行改方法则会可能造成异常，外面保证其安全性")]
        //public void Clear() {
        //    lock (rawBytesLock) {
        //        Array.Resize(ref rawBytes, pageSize);
        //        freeIndex = 0;
        //        freeLists.Clear();
        //        freeSizes.Clear();
        //    }
        //}
        /// <summary>
        /// 获取给定大小的缓冲区。
        /// Gets a buffer of the given size.
        /// </summary>
        /// <param name="bufferSize">要检索的缓冲区的大小。The size of the buffer to retrieve.</param>
        /// <returns>
        /// An <see cref="ArraySegment{T}"/> of <see cref="byte"/>s with <paramref name="bufferSize"/> elements.
        /// </returns>
        /// <remarks>This method is thread-safe.</remarks>
        public ArraySegment<byte> GetBuffer(int bufferSize) {
            ArraySegment<byte> buffer;

            // Round up to the nearest multiple of blockSize.
            var segmentSize = GetSegmentSize(bufferSize);

            if (freeSizes.Contains(segmentSize)) {
                // A buffer of the requested size already exists, so use it.
                var freeList = freeLists[segmentSize];
                lock (freeList) {
                    buffer = freeList.Dequeue();
                    if (buffer.Count != bufferSize) {
                        buffer = new ArraySegment<byte>(rawBytes, buffer.Offset, bufferSize);
                        buffersRecreated++;
                    }
                    if (freeList.Count == 0) {
                        // If this is the last buffer of this size, remove the size from freeSizes.
                        freeSizes.Remove(segmentSize);
                    }
                }
            }
            else {
                //我们没有可用的请求大小的缓冲区，请创建一个新的缓冲区
                // We don't have a buffer of the requested size available, so create a new one.
                lock (rawBytesLock) {
                    // First make sure there is enough space. Resize our array if there isn't.
                    var requiredBytes = bufferSize - (rawBytes.Length - freeIndex + 1);
                    if (requiredBytes > 0) {
                        var pages = requiredBytes / pageSize + ((requiredBytes % pageSize == 0) ? 0 : 1);
                        int newSize = rawBytes.Length + (pages * pageSize);
                        if (newSize > pageSizeMax * pageSizeMaxMultiple) {
                            //UnityEngine.Debug.LogError("申请缓存超出最大缓存值。");
                            return new ArraySegment<byte>();
                        }
                        //注意，一旦调整大小，旧数组被外面拿住Array对象更新问题
                        Array.Resize(ref rawBytes, newSize);
                        resizes++;
                    }

                    // Create the new buffer (make sure the freeIndex is updated to reflect this!)
                    buffer = new ArraySegment<byte>(rawBytes, freeIndex, bufferSize);
                    freeIndex += segmentSize;
                    buffersCreated++;
                }
            }
            Interlocked.Increment(ref buffersRequested);
            return buffer;
        }

        /// <summary>
        /// 释放对象必须是当前缓存取出来的对象
        /// Releases the given buffer, returning it to the pool.
        /// 一旦释放缓冲区，它就会被清除，并且释放的客户端不应该再次使用它。
        /// </summary>
        /// <param name="buffer">The buffer to return to the pool.</param>
        /// <remarks>
        /// Once a buffer is released it is cleared, and should not be used again by the releasing client.
        /// </remarks>
        /// <remarks>This method is thread-safe.</remarks>
        public void ReleaseBuffer(ArraySegment<byte> buffer) {
            var segmentSize = GetSegmentSize(buffer.Count);
            //TODO:执行clear()后检测关联ArraySegment
            if (buffer.Offset + buffer.Count > buffer.Array.Length) {
                //注意：执行清理后，原始数组长度发生变化了，这里的释放                
                return;
            }
            // See if we need to create a new free list to hold the released buffer.
            if (!freeLists.ContainsKey(segmentSize)) {
                freeLists[segmentSize] = new Queue<ArraySegment<byte>>();
            }

            // Clear the contents of the buffer before adding to the free list.
            Array.Clear(buffer.Array, buffer.Offset, buffer.Count);

            // Put the buffer in the free list, and indicate that a buffer of its size is available.
            var freeList = freeLists[segmentSize];
            lock (freeList) {
                freeList.Enqueue(buffer);
                freeSizes.Add(segmentSize);
                buffersReleased++;
            }
        }

        /// <summary>
        /// 获取给定缓冲区大小的段大小
        /// Gets the segment size for the given buffer size.
        /// </summary>
        /// <param name="bufferSize">请求的缓冲区大小（字节为单位）The requested buffer size, in bytes.</param>
        /// <returns>
        /// 段大小，至少是bufferSize,并且是blockSize的倍数
        /// The segment size, which is at least <paramref name="bufferSize"/>, and is a multiple of 
        /// <see cref="blockSize"/>.
        /// </returns>
        private int GetSegmentSize(int bufferSize) {
            return (bufferSize + (blockSize - 1)) & (-blockSize);
        }

        #region Metrics
        /// <summary>
        /// 创建次数
        /// </summary>
        private int buffersCreated;
        /// <summary>
        /// 重新创建次数
        /// </summary>
        private int buffersRecreated;
        /// <summary>
        /// 请求次数
        /// </summary>
        private int buffersRequested;
        /// <summary>
        /// 释放次数
        /// </summary>
        private int buffersReleased;
        /// <summary>
        /// 原始字节数组rawBytes调整大小次数
        /// </summary>
        private int resizes;

        internal void PrintMetrics(TextWriter writer) {
            writer.WriteLine($"Page Size: {pageSize}");
            writer.WriteLine($"Block Size: {blockSize}");
            writer.WriteLine($"Free Lists: {freeLists.Count}");
            writer.WriteLine($"Size: {rawBytes.Length} bytes");
            writer.WriteLine($"Resizes: {resizes}");
            writer.WriteLine($"Created: {buffersCreated}");
            writer.WriteLine($"Re-created: {buffersRecreated}");
            writer.WriteLine($"Requested: {buffersRequested}");
            writer.WriteLine($"Released: {buffersReleased}");
            writer.WriteLine("Reuse: {0:p}", ((double)(buffersRequested - (buffersCreated + buffersRecreated))) / buffersRequested);
        }

        #endregion



        static void _test() {
            // The number of clients to simulate.
            const int NumClients = 10;

            // The number of trials to run.
            const int NumTrials = 10000;

            // Used to randomly request buffers with a fixed set of sizes.
            var rand = new Random();
            var messageSizes = new[] { 31, 60, 63, 121, 250, 501, 1001 };

            // Configure the pool.
            var pool = new BufferPool(pageSize: 1024, pageSizeMax: 1024 * 1024 * 8, blockSize: 32);
            var v = pool.GetBuffer(100);

            // Each client needs to cache a buffer.
            var clients = new ArraySegment<byte>[NumClients];

            for (var trial = 0; trial < NumTrials; trial++) {
                // Get the buffers.
                for (var clientIndex = 0; clientIndex < NumClients; clientIndex++) {
                    var messageSizeIndex = rand.Next(messageSizes.Length);
                    var buffer = pool.GetBuffer(messageSizes[messageSizeIndex]);
                    Debug.Assert(buffer.Count == messageSizes[messageSizeIndex]);
                    for (var i = 0; i < buffer.Count; i++) {
                        buffer.Array[i + buffer.Offset] = 1;
                    }
                    clients[clientIndex] = buffer;
                }

                // Release the buffers.
                for (var clientIndex = 0; clientIndex < NumClients; clientIndex++) {
                    // Simulate some clients hanging onto their buffer.
                    var release = rand.Next(10000);
                    if (release > -1) // adjust odds of not releasing here
                    {
                        pool.ReleaseBuffer(clients[clientIndex]);
                    }
                }
            }
            pool.PrintMetrics(Console.Out);
        }
    }

}
