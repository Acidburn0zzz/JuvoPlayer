// Copyright (c) 2017 Samsung Electronics Co., Ltd All Rights Reserved
// PROPRIETARY/CONFIDENTIAL 
// This software is the confidential and proprietary
// information of SAMSUNG ELECTRONICS ("Confidential Information"). You shall
// not disclose such Confidential Information and shall use it only in
// accordance with the terms of the license agreement you entered into with
// SAMSUNG ELECTRONICS. SAMSUNG make no representations or warranties about the
// suitability of the software, either express or implied, including but not
// limited to the implied warranties of merchantability, fitness for a
// particular purpose, or non-infringement. SAMSUNG shall not be liable for any
// damages suffered by licensee as a result of using, modifying or distributing
// this software or its derivatives.

using JuvoPlayer.Common;
using JuvoPlayer.FFmpeg;
using System;
using System.IO;
using Tizen.Applications;

namespace JuvoPlayer.RTSP
{
    public class RTSPDataProviderFactory : IDataProviderFactory
    {

        public RTSPDataProviderFactory()
        {
        }

        public IDataProvider Create(ClipDefinition clip)
        {
            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip), "clip cannot be null");
            }

            if (!SupportsClip(clip))
            {
                throw new ArgumentException("unsupported clip type");
            }

            var sharedBuffer = new FramesSharedBuffer();
            var rtspClient = new RTSPClient(sharedBuffer);

            var libPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Application.Current.ApplicationInfo.ExecutablePath)), "lib");
            var demuxer = new FFmpegDemuxer(libPath, sharedBuffer);

            return new RTSPDataProvider(demuxer, rtspClient, clip);
        }

        public bool SupportsClip(ClipDefinition clip)
        {
            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip), "clip cannot be null");
            }

            return string.Equals(clip.Type, "Rtp", StringComparison.CurrentCultureIgnoreCase)
                || string.Equals(clip.Type, "Rtsp", StringComparison.CurrentCultureIgnoreCase);
        }
    }
}