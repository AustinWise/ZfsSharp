#region license

/*
Copyright (c) 2013-2017, Milosz Krajewski
All rights reserved.

Redistribution and use in source and binary forms, with or without modification, are permitted provided 
that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this list of conditions 
  and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice, this list of conditions 
  and the following disclaimer in the documentation and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED 
WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR 
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE 
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT 
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN 
IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

#endregion

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace LZ4
{
    /// <summary>
    ///     LZ4 codec selecting best implementation depending on platform.
    /// </summary>
    public static partial class LZ4Codec
    {
        #region fields

        /// <summary>Encoding service.</summary>
        private static readonly ILZ4Service Encoder;

        /// <summary>Encoding service for HC algorithm.</summary>
        private static readonly ILZ4Service EncoderHC;

        /// <summary>Decoding service.</summary>
        private static readonly ILZ4Service Decoder;

        // ReSharper disable InconsistentNaming

        // safe c#
        private static ILZ4Service _service_S32;
        private static ILZ4Service _service_S64;

        // ReSharper restore InconsistentNaming

        #endregion

        #region initialization

        /// <summary>Initializes the <see cref="LZ4Codec" /> class.</summary>
        static LZ4Codec()
        {
            InitializeLZ4s();

            ILZ4Service encoder, decoder, encoderHC;
            SelectCodec(out encoder, out decoder, out encoderHC);

            Encoder = encoder;
            Decoder = decoder;
            EncoderHC = encoderHC;

            if (Encoder == null || Decoder == null)
            {
                throw new NotSupportedException("No LZ4 compression service found");
            }
        }

        private static void SelectCodec(out ILZ4Service encoder, out ILZ4Service decoder, out ILZ4Service encoderHC)
        {
            // refer to: http://lz4net.codeplex.com/wikipage?title=Performance%20Testing for explanation about this order
            // feel free to change preferred order, just don't do it willy-nilly back it up with some evidence
            // it has been tested for Intel on Microsoft .NET only but looks reasonable for Mono as well
            if (IntPtr.Size == 4)
            {
                encoder =
                    _service_S32 ??
                    _service_S64;
                decoder =
                    _service_S64 ??
                    _service_S32;
                encoderHC =
                    _service_S32 ??
                    _service_S64;
            }
            else
            {
                encoder =
                    _service_S32 ??
                    _service_S64;
                decoder =
                    _service_S64 ??
                    _service_S32;
                encoderHC =
                    _service_S32 ??
                    _service_S64;
            }
        }

        /// <summary>Tries to execute specified action. Ignores exception if it failed.</summary>
        /// <param name="method">The method.</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Try(Action method)
        {
            try
            {
                method();
            }
            catch
            {
                // ignore exception
            }
        }

        /// <summary>Tries to execute specified action. Ignores exception if it failed.</summary>
        /// <typeparam name="T">Type of result.</typeparam>
        /// <param name="method">The method.</param>
        /// <param name="defaultValue">The default value, returned when action fails.</param>
        /// <returns>Result of given method, or default value.</returns>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static T Try<T>(Func<T> method, T defaultValue)
        {
            try
            {
                return method();
            }
            catch
            {
                return defaultValue;
            }
        }

        #endregion

        #region public interface

        /// <summary>Gets the name of selected codec(s).</summary>
        /// <value>The name of the codec.</value>
        public static string CodecName
        {
            get
            {
                return string.Format(
                    "{0}/{1}/{2}HC",
                    Encoder == null ? "<none>" : Encoder.CodecName,
                    Decoder == null ? "<none>" : Decoder.CodecName,
                    EncoderHC == null ? "<none>" : EncoderHC.CodecName);
            }
        }

        /// <summary>Get maximum output length.</summary>
        /// <param name="inputLength">Input length.</param>
        /// <returns>Output length.</returns>
        public static int MaximumOutputLength(int inputLength)
        {
            return inputLength + (inputLength / 255) + 16;
        }

        #region Decode

        /// <summary>Decodes the specified input.</summary>
        /// <param name="input">The input.</param>
        /// <param name="inputOffset">The input offset.</param>
        /// <param name="inputLength">Length of the input.</param>
        /// <param name="output">The output.</param>
        /// <param name="outputOffset">The output offset.</param>
        /// <param name="outputLength">Length of the output.</param>
        /// <param name="knownOutputLength">Set it to <c>true</c> if output length is known.</param>
        /// <returns>Number of bytes written.</returns>
        public static int Decode(
            byte[] input,
            int inputOffset,
            int inputLength,
            byte[] output,
            int outputOffset,
            int outputLength = 0,
            bool knownOutputLength = false)
        {
            return Decoder.Decode(input, inputOffset, inputLength, output, outputOffset, outputLength, knownOutputLength);
        }

        /// <summary>Decodes the specified input.</summary>
        /// <param name="input">The input.</param>
        /// <param name="inputOffset">The input offset.</param>
        /// <param name="inputLength">Length of the input.</param>
        /// <param name="outputLength">Length of the output.</param>
        /// <returns>Decompressed buffer.</returns>
        public static byte[] Decode(byte[] input, int inputOffset, int inputLength, int outputLength)
        {
            if (inputLength < 0)
                inputLength = input.Length - inputOffset;

            if (input == null)
                throw new ArgumentNullException("input");
            if (inputOffset < 0 || inputOffset + inputLength > input.Length)
                throw new ArgumentException("inputOffset and inputLength are invalid for given input");

            var result = new byte[outputLength];
            var length = Decode(input, inputOffset, inputLength, result, 0, outputLength, true);
            if (length != outputLength)
                throw new ArgumentException("outputLength is not valid");
            return result;
        }

        #endregion

        #endregion
    }
}