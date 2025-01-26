// modify from https://github.com/ValveResourceFormat/ValvePak

namespace Alco.IO;

/// <summary>
/// VPK_ArchiveMD5SectionEntry
/// </summary>
public class ArchiveMD5SectionEntry
{
	public ArchiveMD5SectionEntry(uint archiveIndex, uint offset, uint length, byte[] checksum)
	{
		ArchiveIndex = archiveIndex;
		Offset = offset;
		Length = length;
		Checksum = checksum;
	}

	/// <summary>
	/// Gets or sets the CRC32 checksum of this entry.
	/// </summary>
	public uint ArchiveIndex { get; }

	/// <summary>
	/// Gets or sets the offset in the package.
	/// </summary>
	public uint Offset { get; }

	/// <summary>
	/// Gets or sets the length in bytes.
	/// </summary>
	public uint Length { get; }

	/// <summary>
	/// Gets or sets the expected Checksum checksum.
	/// </summary>
	public byte[] Checksum { get; }
}

