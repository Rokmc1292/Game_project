param(
    [string]$UnityArchive,
    [string]$MasterArchive
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$spec = Get-Content -LiteralPath (Join-Path $root 'SOUND_PRODUCTION_SPEC.md') -Raw -Encoding utf8
$section = (($spec -split '### 5.1 P0')[1] -split '### 5.3')[0]
$expected = @{}
$contracts = @{}
foreach ($match in [regex]::Matches($section, '(?m)^\| `((?:BGM|SFX)_[A-Za-z0-9_]+)` / `([^`]+)` \| ([0-9.]+)~([0-9.]+)[^,]+, (Mono|Stereo)')) {
    $expected[$match.Groups[1].Value] = 'Assets/_Project/Audio/' + $match.Groups[2].Value
    $contracts[$match.Groups[1].Value] = @{
        Min = [double]::Parse($match.Groups[3].Value, [Globalization.CultureInfo]::InvariantCulture)
        Max = [double]::Parse($match.Groups[4].Value, [Globalization.CultureInfo]::InvariantCulture)
        Channels = $(if ($match.Groups[5].Value -eq 'Mono') { 1 } else { 2 })
    }
}
if ($expected.Count -ne 20) { throw 'The production spec must define 20 new audio assets.' }

if (-not ('AudioPcmAudit' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;

public sealed class AudioPcmAudit
{
    public int Bits, Channels, Hz;
    public double Seconds, PeakDbfs, RmsDbfs, LoopEdgeJump;
    public long ClippedSamples;

    public static AudioPcmAudit Read(Stream stream)
    {
        using (var reader = new BinaryReader(stream, Encoding.ASCII, true))
        {
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") throw new Exception("Not RIFF");
            long riffEnd = reader.ReadUInt32() + 8L;
            if (riffEnd != stream.Length) throw new Exception("RIFF length mismatch");
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") throw new Exception("Not WAVE");
            var result = new AudioPcmAudit();
            long dataStart = 0, dataSize = 0;
            int align = 0, byteRate = 0;
            while (stream.Position + 8 <= stream.Length)
            {
                string id = Encoding.ASCII.GetString(reader.ReadBytes(4));
                long size = reader.ReadUInt32();
                long next = stream.Position + size;
                if (next > stream.Length) throw new Exception("Truncated chunk");
                if (id == "fmt ")
                {
                    if (size < 16 || reader.ReadUInt16() != 1) throw new Exception("Not PCM");
                    result.Channels = reader.ReadUInt16();
                    result.Hz = (int)reader.ReadUInt32();
                    byteRate = (int)reader.ReadUInt32();
                    align = reader.ReadUInt16();
                    result.Bits = reader.ReadUInt16();
                }
                if (id == "data") { dataStart = stream.Position; dataSize = size; }
                stream.Position = Math.Min(next + (size % 2), stream.Length);
            }
            if ((result.Bits != 16 && result.Bits != 24) || result.Channels < 1 || result.Channels > 2 ||
                result.Hz != 48000 || align != result.Channels * result.Bits / 8 ||
                byteRate != result.Hz * align || dataSize == 0 || dataSize % align != 0)
                throw new Exception("Invalid PCM format or incomplete frames");
            result.Seconds = (double)dataSize / byteRate;
            stream.Position = dataStart;
            double peak = 0, sum = 0, fullScale = result.Bits == 16 ? 32768.0 : 8388608.0;
            long count = dataSize / (result.Bits / 8);
            var first = new double[result.Channels];
            var last = new double[result.Channels];
            for (long i = 0; i < count; i++)
            {
                int sample;
                if (result.Bits == 16) sample = reader.ReadInt16();
                else
                {
                    sample = reader.ReadByte() | (reader.ReadByte() << 8) | (reader.ReadByte() << 16);
                    if ((sample & 0x800000) != 0) sample |= unchecked((int)0xff000000);
                }
                double value = sample / fullScale;
                if (Math.Abs(value) >= (fullScale - 1) / fullScale) result.ClippedSamples++;
                peak = Math.Max(peak, Math.Abs(value));
                sum += value * value;
                int channel = (int)(i % result.Channels);
                if (i < result.Channels) first[channel] = value;
                last[channel] = value;
            }
            if (peak == 0) throw new Exception("Silent audio");
            result.PeakDbfs = 20 * Math.Log10(peak);
            result.RmsDbfs = 20 * Math.Log10(Math.Sqrt(sum / count));
            for (int c = 0; c < result.Channels; c++)
                result.LoopEdgeJump = Math.Max(result.LoopEdgeJump, Math.Abs(first[c] - last[c]));
            return result;
        }
    }
}
'@
}

$files = @(Get-ChildItem -LiteralPath (Join-Path $root 'Assets/_Project/Audio') -Recurse -File -Filter '*.wav')
if ($files.Count -ne 57) { throw "Expected 57 WAVs, found $($files.Count)." }
$duplicates = @($files | Group-Object BaseName | Where-Object Count -gt 1)
if ($duplicates.Count) { throw 'Duplicate clip names.' }
$results = foreach ($file in $files) {
    $stream = [IO.File]::OpenRead($file.FullName)
    try { $audit = [AudioPcmAudit]::Read($stream) } finally { $stream.Dispose() }
    if ($audit.Bits -ne 16) { throw "Non-16-bit runtime file: $($file.Name)" }
    if ($audit.ClippedSamples -gt 0) { throw "Full-scale samples found: $($file.Name)" }
    if ($expected.ContainsKey($file.BaseName)) {
        $relative = [IO.Path]::GetRelativePath($root, $file.DirectoryName).Replace('\', '/')
        if ($relative -ne $expected[$file.BaseName]) { throw "Wrong folder: $($file.Name)" }
        $contract = $contracts[$file.BaseName]
        if ($audit.Channels -ne $contract.Channels -or $audit.Seconds -lt $contract.Min -or
            $audit.Seconds -gt $contract.Max + 0.001) { throw "Duration/channel differs from spec: $($file.Name)" }
    }
    [pscustomobject]@{
        Name = $file.BaseName
        Seconds = [math]::Round($audit.Seconds, 4)
        Channels = $audit.Channels
        PeakDbfs = [math]::Round($audit.PeakDbfs, 2)
        RmsDbfs = [math]::Round($audit.RmsDbfs, 2)
        LoopEdgeJump = [math]::Round($audit.LoopEdgeJump, 6)
        New = $expected.ContainsKey($file.BaseName)
    }
}
foreach ($name in $expected.Keys) {
    if ($name -notin $files.BaseName) { throw "Missing new cue: $name" }
}

foreach ($pair in @(@($UnityArchive, 16), @($MasterArchive, 24))) {
    if ([string]::IsNullOrEmpty($pair[0])) { continue }
    $zip = [IO.Compression.ZipFile]::OpenRead($pair[0])
    try {
        if ($zip.Entries.Count -ne 20) { throw 'Archive does not contain exactly 20 entries.' }
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($entry in $zip.Entries) {
            $name = [IO.Path]::GetFileNameWithoutExtension($entry.FullName)
            if (-not $expected.ContainsKey($name)) { throw "Unexpected archive entry: $($entry.FullName)" }
            if (-not $seen.Add($name)) { throw "Duplicate archive entry: $name" }
            $memory = [IO.MemoryStream]::new()
            $source = $entry.Open()
            try {
                $source.CopyTo($memory)
                $memory.Position = 0
                $audit = [AudioPcmAudit]::Read($memory)
                if ($audit.Bits -ne $pair[1]) { throw "Unexpected archive bit depth: $name" }
                $contract = $contracts[$name]
                if ($audit.ClippedSamples -gt 0 -or $audit.Channels -ne $contract.Channels -or
                    $audit.Seconds -lt $contract.Min -or $audit.Seconds -gt $contract.Max + 0.001) {
                    throw "Archive audio differs from spec: $name"
                }
                if ($pair[1] -eq 16) {
                    $disk = [IO.File]::ReadAllBytes((Join-Path $root ($expected[$name] + '/' + $name + '.wav')))
                    $archived = $memory.ToArray()
                    if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($disk)) -ne
                        [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($archived))) {
                        throw "Imported WAV differs from the supplied archive: $name"
                    }
                }
            } finally {
                $source.Dispose()
                $memory.Dispose()
            }
        }
        Write-Host "Archive validated ($($pair[1])-bit): 20 assets."
    } finally { $zip.Dispose() }
}

$sources = (Get-ChildItem -LiteralPath (Join-Path $root 'Assets/_Project/Scripts/Presentation') -Filter '*.cs' |
    Get-Content -Raw) -join "`n"
$references = [regex]::Matches($sources, '"((?:SFX|BGM)_[A-Za-z0-9_]+)"') |
    ForEach-Object { $_.Groups[1].Value } | Where-Object { -not $_.EndsWith('_') } | Sort-Object -Unique
foreach ($name in $references) {
    if ($name -notin $files.BaseName) { throw "Missing referenced clip: $name" }
}
$catalog = Get-Content -LiteralPath (Join-Path $root 'Assets/_Project/Resources/AudioCatalog.asset') -Raw
$catalogGuids = @([regex]::Matches($catalog, 'guid: ([a-f0-9]{32})') |
    ForEach-Object { $_.Groups[1].Value })
$clipGuids = foreach ($file in $files) {
    $meta = Get-Content -LiteralPath ($file.FullName + '.meta') -Raw
    $guid = [regex]::Match($meta, '(?m)^guid: ([a-f0-9]{32})').Groups[1].Value
    if (-not $guid -or $guid -notin $catalogGuids) { throw "Missing catalog reference: $($file.Name)" }
    $guid
}
if (@($clipGuids | Sort-Object -Unique).Count -ne 57) { throw 'Duplicate or missing audio meta GUIDs.' }
$results | Where-Object New | Format-Table -AutoSize
Write-Host "PASS: 57 valid non-silent runtime WAVs; 20 new cues; no full-scale samples, duplicate names, or missing references."
Write-Host 'Sample peaks and edge jumps are not true-peak, LUFS, or a substitute for listening.'
