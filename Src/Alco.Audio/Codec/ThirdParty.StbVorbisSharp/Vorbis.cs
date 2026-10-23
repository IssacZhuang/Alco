#nullable disable

using System;
using static StbVorbisSharp.StbVorbis;

namespace StbVorbisSharp
{
	/// <summary>
	/// Decodes an in-memory Ogg Vorbis stream into interleaved 16-bit samples.
	/// </summary>
	public unsafe class Vorbis : IDisposable
	{
		private readonly byte[] _data;
		private readonly stb_vorbis_info _vorbisInfo;

		/// <summary>Gets the native decoder handle; null after disposal.</summary>
		public stb_vorbis StbVorbis { get; private set; }

		/// <summary>Gets the native stream information.</summary>
		public stb_vorbis_info StbVorbisInfo => _vorbisInfo;

		/// <summary>Gets the stream sample rate in hertz.</summary>
		public int SampleRate => (int) _vorbisInfo.sample_rate;

		/// <summary>Gets the number of audio channels.</summary>
		public int Channels => _vorbisInfo.channels;

		/// <summary>Gets the stream duration in seconds.</summary>
		public float LengthInSeconds { get; }

		/// <summary>Gets the buffer that receives decoded samples.</summary>
		public short[] SongBuffer { get; }

		/// <summary>Gets the number of samples produced by the last <see cref="SubmitBuffer"/> call.</summary>
		public int Decoded { get; private set; }

		private Vorbis(byte[] data)
		{
			if (data == null) throw new ArgumentNullException("data");

			_data = data;

			stb_vorbis vorbis;
			fixed (byte* b = data)
			{
				vorbis = stb_vorbis_open_memory(b, data.Length, null);
			}

			StbVorbis = vorbis;
			_vorbisInfo = stb_vorbis_get_info(vorbis);
			LengthInSeconds = stb_vorbis_stream_length_in_seconds(StbVorbis);

			SongBuffer = new short[_vorbisInfo.sample_rate];

			Restart();
		}

		/// <summary>Releases the native decoder.</summary>
		public void Dispose()
		{
			if (StbVorbis != null)
			{
				vorbis_deinit(StbVorbis);
				StbVorbis = null;
			}
		}

		~Vorbis()
		{
			Dispose();
		}

		/// <summary>Seeks the decoder back to the start of the stream.</summary>
		public void Restart()
		{
			stb_vorbis_seek_start(StbVorbis);
		}

		/// <summary>Decodes the next block of samples into <see cref="SongBuffer"/>.</summary>
		public void SubmitBuffer()
		{
			fixed (short* ptr = SongBuffer)
			{
				Decoded = stb_vorbis_get_samples_short_interleaved(StbVorbis, _vorbisInfo.channels, ptr,
					(int) _vorbisInfo.sample_rate);
			}
		}

		/// <summary>Creates a decoder over an in-memory Ogg Vorbis stream.</summary>
		/// <param name="data">The encoded Ogg Vorbis data.</param>
		/// <returns>A decoder for the stream.</returns>
		public static Vorbis FromMemory(byte[] data) => new Vorbis(data);
	}
}