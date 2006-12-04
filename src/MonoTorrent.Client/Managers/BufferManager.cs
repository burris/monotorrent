//
// BufferManager.cs
//
// Authors:
//   Alan McGovern alan.mcgovern@gmail.com
//
// Copyright (C) 2006 Alan McGovern
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//


using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;

namespace MonoTorrent.Client
{
    internal enum BufferType
    {
        SmallMessageBuffer,
        LargeMessageBuffer
    }

    internal class BufferManager
    {
        public const int SmallMessageBufferSize = 256;
        public const int LargeMessageBufferSize = ((1 << 14) + 32);

        public static readonly byte[] EmptyBuffer = new byte[0];
        private Queue<byte[]> smallMessageBuffers;
        private Queue<byte[]> largeMessageBuffers;


        /// <summary>
        /// The class that controls the allocating and deallocating of all byte[] buffers used in the engine.
        /// </summary>
        public BufferManager()
        {
            this.largeMessageBuffers = new Queue<byte[]>();
            this.smallMessageBuffers = new Queue<byte[]>();

            // Preallocate 35 of each buffer to help avoid heap fragmentation due to pinning
            this.AllocateBuffers(35, BufferType.SmallMessageBuffer);
            this.AllocateBuffers(35, BufferType.LargeMessageBuffer);
        }


        /// <summary>
        /// Allocates an existing buffer from the pool
        /// </summary>
        /// <param name="buffer">The byte[]you want the buffer to be assigned to</param>
        /// <param name="type">The type of buffer that is needed</param>
        public void GetBuffer(ref byte[] buffer, BufferType type)
        {
            // We check to see if the buffer already there is the empty buffer. If it isn't, then we have
            // a buffer leak somewhere and the buffers aren't being freed properly.
            if (buffer != EmptyBuffer)
                throw new ArgumentException("The oldBuffer should have been recovered before getting a new buffer");

            // If we're getting a small buffer and there are none in the pool, just return a new one.
            // Otherwise return one from the pool.
            if (type == BufferType.SmallMessageBuffer)
                lock (this.smallMessageBuffers)
                {
                    if (this.smallMessageBuffers.Count == 0)
                        this.AllocateBuffers(8, BufferType.SmallMessageBuffer);
                    buffer = this.smallMessageBuffers.Dequeue();
                }

            // If we're getting a large buffer and there are none in the pool, just return a new one.
            // Otherwise return one from the pool.
            else if (type == BufferType.LargeMessageBuffer)
                lock (this.largeMessageBuffers)
                {
                    if (this.largeMessageBuffers.Count == 0)
                        this.AllocateBuffers(8, BufferType.LargeMessageBuffer);
                    buffer = this.largeMessageBuffers.Dequeue();
                }

            else
                throw new ArgumentException("Couldn't allocate the required buffer", "type");
        }


        /// <summary>
        /// Returns a buffer to the pool after it has finished being used.
        /// </summary>
        /// <param name="buffer">The buffer to add back into the pool</param>
        /// <returns></returns>
        public void FreeBuffer(ref byte[] buffer)
        {
            // If true, the buffer has already been freed, so we just return
            if (buffer == EmptyBuffer)
                return;

            // If the buffer is a small buffer, add it into that smallbuffer queue
            if (buffer.Length == SmallMessageBufferSize)
                lock (this.smallMessageBuffers)
                    this.smallMessageBuffers.Enqueue(buffer);

            // If the buffer is a large buffer, add it into the largebuffer queue
            else if (buffer.Length == LargeMessageBufferSize)
                lock (this.largeMessageBuffers)
                    this.largeMessageBuffers.Enqueue(buffer);

            // All buffers should be allocated in this class, so if something else is passed in that isn't the right size
            // We just throw an exception as someone has done something wrong.
            else
                throw new Exception("That buffer wasn't created by this manager");

            buffer = EmptyBuffer; // After recovering the buffer, we send the "EmptyBuffer" back as a placeholder
        }


        private void AllocateBuffers(int number, BufferType type)
        {
            while (number-- > 0)
                if (type == BufferType.LargeMessageBuffer)
                    this.largeMessageBuffers.Enqueue(new byte[LargeMessageBufferSize]);

                else if (type == BufferType.SmallMessageBuffer)
                    this.smallMessageBuffers.Enqueue(new byte[SmallMessageBufferSize]);

                else
                    throw new ArgumentException("Unsupported BufferType detected");
        }
    }
}
