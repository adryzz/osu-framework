﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osuTK.Graphics.ES30;
using osu.Framework.Graphics.OpenGL;
using osu.Framework.Graphics.OpenGL.Textures;
using osu.Framework.Graphics.Textures;
using osu.Framework.Platform;

namespace osu.Framework.Graphics.Video
{
    internal unsafe class VideoTexture : TextureGLSingle
    {
        private List<int> textureIds;
        public int[] TextureIds => textureIds.ToArray();

        public VideoTexture(int width, int height)
            : base(width, height, true, All.Linear)
        {
            memoryLease = NativeMemoryTracker.AddMemory(this, Width * Height * 3 / 2);
        }

        #region Disposal

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            memoryLease?.Dispose();

            GLWrapper.ScheduleDisposal(unload);
        }

        private void unload()
        {
            textureIds.RemoveAll(i => i <= 0);

            for (int i = 0; i < textureIds.Count; i++)
                GL.DeleteTextures(1, new[] { textureIds[i] });

            textureIds = null;
        }

        #endregion

        #region Memory Tracking

        private readonly NativeMemoryTracker.NativeMemoryLease memoryLease;

        #endregion

        public override bool Bind(TextureUnit unit = TextureUnit.Texture0)
        {
            if (!Available)
                throw new ObjectDisposedException(ToString(), "Can not bind a disposed texture.");

            Upload();

            if (textureIds.TrueForAll(i => i <= 0))
                return false;

            GLWrapper.BindTexture(this, unit);

            return true;
        }

        protected override void DoUpload(ITextureUpload upload, IntPtr dataPointer)
        {
            var videoUpload = upload as VideoTextureUpload;

            if (textureIds == null)
                textureIds = new List<int> { 0, 0, 0 };

            // Do we need to generate a new texture?
            if (!textureIds.TrueForAll(i => i > 0) || InternalWidth != Width || InternalHeight != Height)
            {
                InternalWidth = Width;
                InternalHeight = Height;

                // We only need to generate a new texture if we don't have one already. Otherwise just re-use the current one.
                if (!textureIds.TrueForAll(i => i > 0))
                {
                    for (int i = 0; i < textureIds?.Count; i++)
                    {
                        int[] textures = new int[1];
                        GL.GenTextures(1, textures);

                        textureIds[i] = textures[0];

                        GL.BindTexture(TextureTarget.Texture2D, textureIds[i]);
                        GL.TexImage2D(TextureTarget2d.Texture2D, 0, TextureComponentCount.R8,
                            videoUpload.Frame->width / (i > 0 ? 2 : 1), videoUpload.Frame->height / (i > 0 ? 2 : 1), 0, PixelFormat.Red, PixelType.UnsignedByte, IntPtr.Zero);
                        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)All.Linear);
                        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)All.Linear);

                        UpdateWrapMode();
                    }
                }
            }

            for (int i = 0; i < textureIds?.Count; i++)
            {
                GL.BindTexture(TextureTarget.Texture2D, textureIds[i]);
                GL.PixelStore(PixelStoreParameter.UnpackRowLength, videoUpload.Frame->linesize[(uint)i]);
                GL.TexSubImage2D(TextureTarget2d.Texture2D, 0, 0, 0, videoUpload.Frame->width / (i > 0 ? 2 : 1), videoUpload.Frame->height / (i > 0 ? 2 : 1),
                    PixelFormat.Red, PixelType.UnsignedByte, (IntPtr)videoUpload.Frame->data[(uint)i]);
            }

            GL.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
        }
    }
}
