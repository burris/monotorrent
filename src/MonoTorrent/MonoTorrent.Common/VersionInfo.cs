//
// VersionInfo.cs
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
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace MonoTorrent.Common
{
    public static class VersionInfo
    {
        /// <summary>
        /// Protocol string for version 1.0 of Bittorrent Protocol
        /// </summary>
        public static readonly string ProtocolStringV100 = "BitTorrent protocol";

        /// <summary>
        /// The current version of the client
        /// </summary>
        public static readonly string ClientVersion = CreateClientVersion ();

        public static readonly string DhtClientVersion = "MO06";

		static string CreateClientVersion ()
		{
			AssemblyInformationalVersionAttribute version;
			Assembly assembly = Assembly.GetExecutingAssembly ();
			version = (AssemblyInformationalVersionAttribute) assembly.GetCustomAttributes (typeof (AssemblyInformationalVersionAttribute), false)[0];
			Version v = new Version(version.InformationalVersion);

			    // 'MO' for MonoTorrent then four digit version number
			return string.Format ("-MO{0}{1}{2}{3}-",
			                      Math.Max (v.Major, 0),
			                      Math.Max (v.Minor, 0), 
			                      Math.Max (v.Build, 0),
			                      Math.Max (v.Revision, 0));
		}
    }
}
