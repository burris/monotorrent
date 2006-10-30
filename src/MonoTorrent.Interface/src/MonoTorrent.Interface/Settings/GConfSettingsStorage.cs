/*
 * $Id: GConfSettingsStorage.cs 880 2006-08-19 22:50:54Z piotr $
 * Copyright (c) 2006 by Piotr Wolny <gildur@gmail.com>
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 */

using System;
using GConf;
namespace MonoTorrent.Interface.Settings
{
    [Obsolete("Not crossplatform, so don't use it")]
    public class GConfSettingsStorage : ISettingsStorage
    {
        private string baseKey;

        private GConf.Client client;

        public GConfSettingsStorage(string baseKey)
        {
            this.baseKey = baseKey;
            this.client = new GConf.Client();
        }

        public void Store(string key, object val)
        {
            client.Set(baseKey + key, val);
        }

        public object Retrieve(string key)
        {
            return client.Get(baseKey + key);
        }

        public void Flush()
        {
            // No flushing needed
        }
    }
}
