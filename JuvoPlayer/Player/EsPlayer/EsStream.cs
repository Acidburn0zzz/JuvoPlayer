﻿// Copyright (c) 2017 Samsung Electronics Co., Ltd All Rights Reserved
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

using System;
using System.Collections.Generic;
using System.Text;
using System.Collections.Concurrent;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using JuvoLogger;
using JuvoPlayer.Common;
using ESPlayer = Tizen.TV.Multimedia.ESPlayer;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using MpdParser.Node.Writers;

namespace JuvoPlayer.Player.EsPlayer
{
    /// <summary>
    /// Packet submit exception. Raised when packet push to ESPlayer failed in a terminal
    /// way.
    /// </summary>
    [Serializable]
    internal class PacketSubmitException : Exception
    {
        //
        // For guidelines regarding the creation of new exception types, see
        //    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/cpgenref/html/cpconerrorraisinghandlingguidelines.asp
        // and
        //    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/dncscol/html/csharp07192001.asp
        //
        public ESPlayer.SubmitStatus SubmitStatus { get; internal set; }

        public PacketSubmitException(ESPlayer.SubmitStatus status)
        {
            SubmitStatus = status;
        }

        public PacketSubmitException(string message, ESPlayer.SubmitStatus status) : base(message)
        {
            SubmitStatus = status;
        }

        public PacketSubmitException(string message, Exception inner, ESPlayer.SubmitStatus status) : base(message, inner)
        {
            SubmitStatus = status;
        }

        protected PacketSubmitException(
            SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// Internal EsPlayer event. Called when stream has to be reconfigured
    /// </summary>
    /// <param name="bufferConfigPacket">BufferConfigurationPacket</param>
    internal delegate void StreamReconfigure(BufferConfigurationPacket bufferConfigPacket);

    /// <summary>
    /// Class representing and individual stream being transferred
    /// </summary>
    internal class EsStream : IDisposable
    {
        private readonly ILogger logger = LoggerManager.GetInstance().GetLogger("JuvoPlayer");

        /// <summary>
        /// Delegate holding PushConfigMethod.
        /// Different for Audio and Video
        /// </summary>
        private delegate void StreamConfigure(Common.StreamConfig streamConfig);
        private readonly StreamConfigure PushStreamConfig;

        /// <summary>
        /// Reference to ES Player
        /// </summary>
        private readonly ESPlayer.ESPlayer player;

        /// <summary>
        /// Reference to packet storage
        /// </summary>
        private readonly EsPlayerPacketStorage packetStorage;

        /// <summary>
        /// Reference to task responsible for transferring data
        /// between packet storage and ES Player
        /// </summary>
        private Task transferTask;

        /// <summary>
        /// Cancellation source responsible for terminating
        /// transfer task.
        /// </summary>
        private CancellationTokenSource transferCts;

        /// <summary>
        /// Stream Type as known by Juvo Player
        /// </summary>
        private readonly Common.StreamType streamTypeJuvo;

        /// <summary>
        /// Stream Type as known by ESPlayer. Corresponds to
        /// Juvo Player stream type
        /// </summary>
        private readonly ESPlayer.StreamType streamTypeEsPlayer;

        /// <summary>
        /// Currently used stream configuration
        /// </summary>
        private Common.StreamConfig currentConfig;

        /// <summary>
        /// Flag indicating if stream has been configured
        /// </summary>
        public bool IsConfigured => (currentConfig != null);

        /// <summary>
        /// Flag indicating if stream is transferring data between
        /// packet storage and ESPlayer
        /// </summary>
        public bool IsRunning => (transferTask != null);

        /// <summary>
        /// lock object used for serialization of internal operations
        /// that can be accessed externally
        /// </summary>
        private readonly object syncLock = new object();

        /// <summary>
        /// Event - Invoked when stream requires reconfiguration
        /// </summary>
        public event StreamReconfigure ReconfigureStream;

        #region Public API

        public EsStream(ESPlayer.ESPlayer player, Common.StreamType type)
        {
            this.player = player;
            streamTypeJuvo = type;
            packetStorage = EsPlayerPacketStorage.GetInstance();

            switch (streamTypeJuvo)
            {
                case StreamType.Audio:
                    PushStreamConfig = PushAudioConfig;
                    streamTypeEsPlayer = ESPlayer.StreamType.Audio;
                    break;
                case StreamType.Video:
                    PushStreamConfig = PushVideoConfig;
                    streamTypeEsPlayer = ESPlayer.StreamType.Video;
                    break;
                default:
                    logger.Warn($"Unsupported stream type: {streamTypeJuvo}");
                    break;
            }
        }

        /// <summary>
        /// Sets Stream configuration
        /// Non configured stream - stream config will be pushed directly to ES Player.
        /// Configured stream - stream config will be enqueue in packet storage
        /// and processed once retrieved.
        /// </summary>
        /// <param name="bufferConfig">BufferConfigurationPacket</param>
        /// <returns>bool
        /// True - Config Pushed
        /// False - Config Enqueued</returns>
        public bool SetStreamConfig(BufferConfigurationPacket bufferConfig)
        {
            logger.Info($"{streamTypeJuvo}: Already Configured: {IsConfigured}");

            if (IsConfigured)
            {
                packetStorage.AddPacket(bufferConfig);
                logger.Info($"{streamTypeJuvo}: New configuration queued");
                return false;
            }

            PushStreamConfig(bufferConfig.Config);
            return true;
        }

        /// <summary>
        /// Public API for starting data transfer
        /// </summary>
        public void Start()
        {
            StartTransfer();
        }

        /// <summary>
        /// Public API for stopping data transfer
        /// </summary>
        public void Stop()
        {
            StopTransfer();
        }

        /// <summary>
        /// Public API for disabling data transfer. Once called, no further
        /// data transfer will be possible.
        /// </summary>
        public void Disable()
        {
            DisableTransfer();
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Audio Configuration push method.
        /// </summary>
        /// <param name="streamConfig">Common.StreamConfig</param>
        private void PushAudioConfig(Common.StreamConfig streamConfig)
        {
            logger.Info($"{streamTypeJuvo}");
            AudioStreamConfig audioConfig = streamConfig as Common.AudioStreamConfig;

            if (audioConfig == null)
            {
                logger.Error("Invalid stream configuration. Not audio.");
                return;
            }

            logger.Info($"{streamTypeJuvo}: Pushing Stream Config");

            var config = new ESPlayer.AudioStreamInfo
            {
                codecData = audioConfig.CodecExtraData,
                mimeType = EsPlayerUtils.GetCodecMimeType(audioConfig.Codec),
                sampleRate = audioConfig.SampleRate,
                channels = audioConfig.ChannelLayout
            };

            // Set config before calling add streams - resulting events
            // may be processed at any time after
            currentConfig = streamConfig;

            if (!player.AddStream(config))
            {
                logger.Warn($"Failed to set config {streamTypeJuvo}");
                currentConfig = null;
                return;
            }

            logger.Info($"{streamTypeJuvo} Stream configuration set");
        }

        /// <summary>
        /// Video Configuration push method.
        /// </summary>
        /// <param name="streamConfig">Common.StreamConfig</param>
        private void PushVideoConfig(Common.StreamConfig streamConfig)
        {
            logger.Info($"{streamTypeJuvo}");

            VideoStreamConfig videoConfig = streamConfig as Common.VideoStreamConfig;

            if (videoConfig == null)
            {
                logger.Error("Invalid stream configuration. Not video");
                return;
            }

            logger.Info($"{streamTypeJuvo}: Pushing Stream Config");

            var config = new ESPlayer.VideoStreamInfo
            {
                codecData = videoConfig.CodecExtraData,
                mimeType = EsPlayerUtils.GetCodecMimeType(videoConfig.Codec),
                width = videoConfig.Size.Width,
                maxWidth = videoConfig.Size.Width,
                height = videoConfig.Size.Height,
                maxHeight = videoConfig.Size.Height,
                num = videoConfig.FrameRateNum,
                den = videoConfig.FrameRateDen
            };

            // Set config before calling add streams - resulting events
            // may be processed at any time after
            currentConfig = streamConfig;

            if (!player.AddStream(config))
            {
                logger.Warn($"Failed to set config {streamTypeJuvo}");
                currentConfig = null;

                return;
            }

            logger.Info($"{streamTypeJuvo} Stream configuration set");
        }



        /// <summary>
        /// Starts data transfer, if not already running, by starting
        /// transfer task.
        /// </summary>
        private void StartTransfer()
        {
            logger.Info($"{streamTypeJuvo}");

            lock (syncLock)
            {
                if (IsRunning)
                {
                    logger.Info($"{streamTypeJuvo}: Already started: {transferTask.Status}");
                    return;
                }

                if (!IsConfigured)
                {
                    logger.Warn($"{streamTypeJuvo}: Not Configured");
                    return;
                }

                transferCts = new CancellationTokenSource();
                var token = transferCts.Token;

                transferTask = Task.Factory.StartNew(() => TransferTask(token), token);
            }
        }

        /// <summary>
        /// Stops data transfer, if already running, by terminating transfer task.
        /// </summary>
        private void StopTransfer()
        {
            logger.Info($"{streamTypeJuvo}");

            lock (syncLock)
            {
                if (!IsRunning)
                {
                    logger.Info($"{streamTypeJuvo}: Already stopped");
                    return;
                }

                logger.Info($"{streamTypeJuvo}: Stopping transfer");

                transferCts.Cancel();
                transferCts.Dispose();
                transferTask = null;
            }
        }

        /// <summary>
        /// Disables further data transfer. Existing data in queue will continue
        /// to be pushed to the player.
        /// </summary>
        private void DisableTransfer()
        {
            logger.Info($"{streamTypeJuvo}");

            packetStorage.Disable(streamTypeJuvo);
        }

        /// <summary>
        /// Transfer task. Retrieves data from underlying storage and pushes it down
        /// to ESPlayer
        /// </summary>
        /// <param name="token">CancellationToken</param>
        private void TransferTask(CancellationToken token)
        {
            logger.Info($"{streamTypeJuvo}: Transfer task started");

            var doDisable = false;

            try
            {
                do
                {
                    var packet = packetStorage.GetPacket(streamTypeJuvo, token);

                    switch (packet)
                    {
                        case Packet eosPacket when eosPacket.IsEOS:
                            PushEosPacket(token);
                            DisableTransfer();
                            break;
                        case BufferConfigurationPacket bufferConfigPacket:

                            logger.Info($"{streamTypeJuvo} Buffer Reconfiguration not supported");
                            return;

                            // Remove current configuration
                            currentConfig = null;
                            ReconfigureStream?.Invoke(bufferConfigPacket);

                            // exit transfer task. This will prevent further transfers
                            // Stops/Restarts will be called by reconfiguration handler.
                            return;

                        case EncryptedPacket encryptedPacket:
                            logger.Info($"{streamTypeJuvo}: Encrypted packet not implemented");
                            break;
                        case Packet dataPacket when packet.IsEOS == false:
                            PushDataPacket(dataPacket, token);
                            break;
                    }

                    packet.Dispose();

                } while (!token.IsCancellationRequested);
            }
            catch (InvalidOperationException)
            {
                logger.Info($"{streamTypeJuvo}: Stream completed");
                doDisable = true;
            }
            catch (OperationCanceledException)
            {
                logger.Info($"{streamTypeJuvo}: Transfer stopped");

                // Operation is cancelled thus Stop/StopInternal has already been
                // called. No need to repeat.
            }
            catch (PacketSubmitException pse)
            {
                logger.Error($"{streamTypeJuvo}: Submit Error " + pse.SubmitStatus);
                doDisable = true;
            }
            finally
            {
                if (doDisable)
                    DisableTransfer();

                logger.Info($"{streamTypeJuvo}: Transfer task terminated");
            }
        }

        /// <summary>
        /// Pushes data packet to ESPlayer
        /// </summary>
        /// <param name="dataPacket">Packet</param>
        /// <param name="token">CancellationToken</param>
        /// <exception cref="PacketSubmitException">
        /// Exception thrown on submit error
        /// </exception>
        private void PushDataPacket(Packet dataPacket, CancellationToken token)
        {
            bool doRetry;

            // Convert Juvo packet to ESPlayer packet
            var esPacket = dataPacket.ToESPlayerPacket(streamTypeEsPlayer);

            // Continue pushing packet till success or terminal failure
            do
            {
                var res = player.SubmitPacket(esPacket);
                doRetry = ProcessPushResult(res, token);

            } while (doRetry);
        }

        /// <summary>
        /// Pushes EOS packet to ESPlayer
        /// </summary>
        /// <param name="dataPacket">Packet</param>
        /// <param name="token">CancellationToken</param>
        /// <exception cref="PacketSubmitException">
        /// Exception thrown on submit error
        /// </exception>
        private void PushEosPacket(CancellationToken token)
        {
            logger.Info("");

            bool doRetry;

            // Continue pushing packet till success or terminal failure
            do
            {
                var res = player.SubmitEosPacket(streamTypeEsPlayer);
                doRetry = ProcessPushResult(res, token);

            } while (doRetry);
        }

        /// <summary>
        /// Processes packet push result. Returned is an indication if retry
        /// should take place or not
        /// </summary>
        /// <param name="status">ESPlayer.SubmitStatus</param>
        /// <param name="token">CancellationToken</param>
        /// <returns>
        /// True - retry packet push
        /// False - do not retry packet push
        /// </returns>
        /// <exception cref="PacketSubmitException">
        /// Exception thrown on submit error
        /// </exception>
        private bool ProcessPushResult(ESPlayer.SubmitStatus status, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                logger.Info($"{streamTypeJuvo}: Operation Cancelled");
                return false;
            }

            TimeSpan delay;
            switch (status)
            {
                case ESPlayer.SubmitStatus.Success:
                    return false;

                case ESPlayer.SubmitStatus.NotPrepared:
                    logger.Info(status.ToString());
                    delay = TimeSpan.FromSeconds(1);
                    break;

                case ESPlayer.SubmitStatus.Full:
                    delay = TimeSpan.FromMilliseconds(10);
                    break;

                default:
                    throw new PacketSubmitException("Packet Submit Error", status);
            }

            // We are left with Status.Full 
            // For now sleep, however, once buffer events will be 
            // emitted from ESPlayer, they could be used here
            try
            {
                using (var napTime = new ManualResetEventSlim(false))
                {
                    napTime.Wait(delay, token);
                }

            }
            catch (OperationCanceledException)
            {
                logger.Info($"{streamTypeJuvo}: Operation Cancelled");
                return false;
            }

            return true;

        }

        #endregion

        #region IDisposable Support
        private bool isDisposed;
        public void Dispose()
        {
            if (isDisposed)
                return;

            logger.Info(streamTypeJuvo.ToString());

            if (IsRunning)
                StopTransfer();

            isDisposed = true;
        }
        #endregion
    }
}
