// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using ManagedBass;
using ManagedBass.Mix;
using osu.Framework.Bindables;
using osu.Framework.Lists;
using osu.Framework.Statistics;

namespace osu.Framework.Audio.Mixing
{
    /// <summary>
    /// Mixes together multiple <see cref="IAudioChannel"/> into one output via BASSmix.
    /// </summary>
    internal class BassAudioMixer : AudioMixer, IBassAudio, IBassAudioMixer
    {
        /// <summary>
        /// The handle for this mixer.
        /// </summary>
        public int Handle { get; private set; }

        internal readonly List<EffectWithHandle> MixedEffects = new List<EffectWithHandle>();
        private readonly WeakList<IBassAudioChannel> mixedChannels = new WeakList<IBassAudioChannel>();

        private const int frequency = 44100;

        /// <summary>
        /// Creates a new <see cref="BassAudioMixer"/>.
        /// </summary>
        /// <param name="defaultMixer"><inheritdoc /></param>
        public BassAudioMixer(AudioMixer? defaultMixer)
            : base(defaultMixer)
        {
            EnqueueAction(createMixer);
        }

        public override BindableList<IEffectParameter> Effects { get; } = new BindableList<IEffectParameter>();

        protected override void AddInternal(IAudioChannel channel)
        {
            Debug.Assert(CanPerformInline);

            if (!(channel is IBassAudioChannel bassChannel))
                return;

            Debug.Assert(!mixedChannels.Contains(bassChannel));
            mixedChannels.Add(bassChannel);

            if (Handle == 0 || bassChannel.Handle == 0)
                return;

            ((IBassAudioMixer)this).RegisterHandle(bassChannel);
        }

        protected override void RemoveInternal(IAudioChannel channel)
        {
            Debug.Assert(CanPerformInline);

            if (!(channel is IBassAudioChannel bassChannel))
                return;

            if (!mixedChannels.Remove(bassChannel))
                return;

            if (Handle == 0 || bassChannel.Handle == 0)
                return;

            bassChannel.MixerChannelPaused = BassMix.ChannelHasFlag(bassChannel.Handle, BassFlags.MixerChanPause);
            BassMix.MixerRemoveChannel(bassChannel.Handle);
        }

        void IBassAudioMixer.RegisterHandle(IBassAudioChannel channel)
        {
            Debug.Assert(CanPerformInline);
            Debug.Assert(channel.Handle != 0);

            if (Handle == 0)
                return;

            if (!mixedChannels.Contains(channel))
                throw new InvalidOperationException("Channel needs to be added to the mixer first.");

            BassFlags flags = BassFlags.MixerChanBuffer;
            if (channel.MixerChannelPaused)
                flags |= BassFlags.MixerChanPause;

            BassMix.MixerAddChannel(Handle, channel.Handle, flags);
        }

        bool IBassAudioMixer.PlayChannel(IBassAudioChannel channel)
        {
            BassMix.ChannelRemoveFlag(channel.Handle, BassFlags.MixerChanPause);
            return Bass.LastError == Errors.OK;
        }

        bool IBassAudioMixer.PauseChannel(IBassAudioChannel channel)
        {
            BassMix.ChannelAddFlag(channel.Handle, BassFlags.MixerChanPause);
            return Bass.LastError == Errors.OK;
        }

        void IBassAudioMixer.StopChannel(IBassAudioChannel channel)
        {
            BassMix.ChannelAddFlag(channel.Handle, BassFlags.MixerChanPause);
            Bass.ChannelSetPosition(channel.Handle, 0); // resets position and also flushes buffer
        }

        public PlaybackState ChannelIsActive(IBassAudioChannel channel)
        {
            // The audio channel's state tells us whether it's stalled or stopped.
            var state = Bass.ChannelIsActive(channel.Handle);

            // The channel is always in a playing state unless stopped or stalled as it's a decoding channel. Retrieve the true playing state from the mixer channel.
            if (state == PlaybackState.Playing)
                state = BassMix.ChannelHasFlag(channel.Handle, BassFlags.MixerChanPause) ? PlaybackState.Paused : state;

            return state;
        }

        long IBassAudioMixer.GetChannelPosition(IBassAudioChannel channel, PositionFlags mode) => BassMix.ChannelGetPosition(channel.Handle);

        bool IBassAudioMixer.SetChannelPosition(IBassAudioChannel channel, long pos, PositionFlags mode) => BassMix.ChannelSetPosition(channel.Handle, pos, mode);

        void IBassAudioMixer.UnregisterHandle(IBassAudioChannel channel)
        {
            Debug.Assert(CanPerformInline);
            Debug.Assert(channel.Handle != 0);

            Remove(channel, false);
        }

        public void UpdateDevice(int deviceIndex)
        {
            if (Handle == 0)
                createMixer();
            else
                Bass.ChannelSetDevice(Handle, deviceIndex);
        }

        private void createMixer()
        {
            if (Handle != 0)
                return;

            // Make sure that bass is initialised before trying to create a mixer.
            // If not, this will be called again when the device is initialised via UpdateDevice().
            if (!Bass.GetDeviceInfo(Bass.CurrentDevice, out var deviceInfo) || !deviceInfo.IsInitialized)
                return;

            Handle = BassMix.CreateMixerStream(frequency, 2, BassFlags.MixerNonStop | BassFlags.Float);
            if (Handle == 0)
                return;

            // Register all channels that have an active handle, which were added to the mixer prior to it being loaded.
            foreach (var channel in mixedChannels)
            {
                if (channel.Handle != 0)
                    ((IBassAudioMixer)this).RegisterHandle(channel);
            }

            Effects.BindCollectionChanged(onEffectsChanged, true);

            Bass.ChannelPlay(Handle);
        }

        private void onEffectsChanged(object? sender, NotifyCollectionChangedEventArgs e) => EnqueueAction(() =>
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                {
                    Debug.Assert(e.NewItems != null);

                    // Work around BindableList sending initial event start with index -1.
                    int startIndex = Math.Max(0, e.NewStartingIndex);

                    MixedEffects.InsertRange(startIndex, e.NewItems.OfType<IEffectParameter>().Select(eff => new EffectWithHandle(eff)));
                    updateEffects(startIndex, MixedEffects.Count - 1);
                    break;
                }

                case NotifyCollectionChangedAction.Move:
                {
                    EffectWithHandle effect = MixedEffects[e.OldStartingIndex];
                    MixedEffects.RemoveAt(e.OldStartingIndex);
                    MixedEffects.Insert(e.NewStartingIndex, effect);
                    updateEffects(Math.Min(e.OldStartingIndex, e.NewStartingIndex), MixedEffects.Count - 1);
                    break;
                }

                case NotifyCollectionChangedAction.Remove:
                {
                    Debug.Assert(e.OldItems != null);

                    MixedEffects.RemoveRange(e.OldStartingIndex, e.OldItems.Count);
                    updateEffects(e.OldStartingIndex, MixedEffects.Count - 1);
                    break;
                }

                case NotifyCollectionChangedAction.Replace:
                {
                    Debug.Assert(e.NewItems != null);

                    EffectWithHandle oldEffect = MixedEffects[e.NewStartingIndex];
                    MixedEffects[e.NewStartingIndex] = new EffectWithHandle((IEffectParameter?)e.NewItems[0]);
                    removeEffect(oldEffect);
                    updateEffects(e.NewStartingIndex, e.NewStartingIndex);
                    break;
                }

                case NotifyCollectionChangedAction.Reset:
                {
                    foreach (var effect in MixedEffects)
                        removeEffect(effect);
                    MixedEffects.Clear();
                    break;
                }
            }

            void removeEffect(EffectWithHandle effect)
            {
                if (effect.Handle != 0)
                    Bass.ChannelRemoveFX(Handle, effect.Handle);
            }

            void updateEffects(int startIndex, int endIndex)
            {
                for (int i = startIndex; i <= endIndex; i++)
                {
                    var effect = MixedEffects[i];
                    effect.Priority = i;

                    if (effect.Handle != 0)
                        Bass.FXSetPriority(effect.Handle, effect.Priority);
                    else
                    {
                        effect.Handle = Bass.ChannelSetFX(Handle, effect.Effect.FXType, effect.Priority);
                        Bass.FXSetParameters(effect.Handle, effect.Effect);
                    }
                }
            }
        });

        protected override void UpdateState()
        {
            FrameStatistics.Add(StatisticsCounterType.MixChannels, mixedChannels.Count());
            base.UpdateState();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            // Move all contained channels back to the default mixer.
            foreach (var channel in mixedChannels.ToArray())
                Remove(channel);

            if (Handle != 0)
            {
                Bass.StreamFree(Handle);
                Handle = 0;
            }
        }

        internal class EffectWithHandle
        {
            public int Handle { get; set; }
            public int Priority { get; set; }

            public readonly IEffectParameter Effect;

            public EffectWithHandle(IEffectParameter? effect)
            {
                Effect = effect ?? throw new ArgumentNullException(nameof(effect));
            }
        }
    }
}
