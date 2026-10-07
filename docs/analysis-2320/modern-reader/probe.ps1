param([string]$Version)
$ErrorActionPreference='Stop'
$w=$PSScriptRoot
$dll="$w\dll\x64\StormLib.dll"
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Probe {
 [DllImport(@"$dll", CharSet=CharSet.Unicode, SetLastError=true)] [return:MarshalAs(UnmanagedType.I1)] public static extern bool SFileOpenArchive(string p,uint priority,uint flags,out IntPtr h);
 [DllImport(@"$dll", CharSet=CharSet.Ansi, SetLastError=true)] [return:MarshalAs(UnmanagedType.I1)] public static extern bool SFileOpenFileEx(IntPtr h,string name,uint scope,out IntPtr f);
 [DllImport(@"$dll",SetLastError=true)] public static extern uint SFileGetFileSize(IntPtr f,out uint high);
 [DllImport(@"$dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.I1)] public static extern bool SFileReadFile(IntPtr f,byte[] b,uint n,out uint read,IntPtr overlap);
 [DllImport(@"$dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.I1)] public static extern bool SFileGetFileInfo(IntPtr f,int info,out uint value,uint size,out uint needed);
 [DllImport(@"$dll")] [return:MarshalAs(UnmanagedType.I1)] public static extern bool SFileCloseFile(IntPtr f);
 [DllImport(@"$dll")] [return:MarshalAs(UnmanagedType.I1)] public static extern bool SFileCloseArchive(IntPtr h);
}
"@
$map="C:\Users\123\Documents\Warcraft III\Maps\Download\ORDR_S2_$Version[R].w3x"
$hashBefore=(Get-FileHash -LiteralPath $map).Hash
$report=[ordered]@{version=$Version;reader='official v9.40 release stormlib_dll.zip x64';flags='0x100 STREAM_FLAG_READ_ONLY only';x64=[Environment]::Is64BitProcess;archive=$map;sha256Before=$hashBefore;opened=$false;members=@()}
$ha=[IntPtr]::Zero
$ok=[Probe]::SFileOpenArchive($map,0,0x100,[ref]$ha); $err=[Runtime.InteropServices.Marshal]::GetLastWin32Error()
$report.opened=$ok; $report.openError=if($ok){0}else{$err}
if($ok){
 try {
  foreach($pair in @(@('sectorSize',35),@('archiveFlags',39),@('fileTableSize',34))){$v=[uint32]0;$n=[uint32]0;if([Probe]::SFileGetFileInfo($ha,$pair[1],[ref]$v,4,[ref]$n)){$report[$pair[0]]=$v}}
  foreach($name in @('war3map.j','war3map.w3u','war3map.w3a','war3map.wts','war3mapMisc.txt')){
   $row=[ordered]@{name=$name}; $hf=[IntPtr]::Zero
   $ok=[Probe]::SFileOpenFileEx($ha,$name,0,[ref]$hf);$err=[Runtime.InteropServices.Marshal]::GetLastWin32Error();$row.opened=$ok
   if(!$ok){$row.error=$err;$report.members+=,$row;continue}
   try {
    $hi=[uint32]0; $size=[Probe]::SFileGetFileSize($hf,[ref]$hi);$row.size=$size;$v=[uint32]0;$n=[uint32]0
    if([Probe]::SFileGetFileInfo($hf,53,[ref]$v,4,[ref]$n)){$row.flags=('0x{0:X8}' -f $v)}
    if($hi -ne 0 -or $size -gt 64MB){$row.status='skipped over 64 MiB bound'} else {
     $dir=Join-Path $w "members-$Version"; New-Item -ItemType Directory -Force $dir|Out-Null
     $out=Join-Path $dir $name;$stream=[IO.File]::Open($out,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write);$buf=[byte[]]::new(65536);$total=0
     try {while($total -lt $size){$read=[uint32]0;$ask=[uint32][Math]::Min($buf.Length,$size-$total);$ok=[Probe]::SFileReadFile($hf,$buf,$ask,[ref]$read,[IntPtr]::Zero);$err=[Runtime.InteropServices.Marshal]::GetLastWin32Error();if(!$ok -or $read -eq 0){throw "Read failed error=$err read=$read at=$total"};$stream.Write($buf,0,$read);$total+=$read}}finally{$stream.Dispose()}
     $row.status='read';$row.readBytes=$total;$row.sha256=(Get-FileHash -LiteralPath $out).Hash
    }
   } catch {$row.status='failed';$row.error=$_.ToString()} finally {[Probe]::SFileCloseFile($hf)|Out-Null}
   $report.members+=,$row
  }
 } finally {[Probe]::SFileCloseArchive($ha)|Out-Null}
}
$report.sha256After=(Get-FileHash -LiteralPath $map).Hash
$report | ConvertTo-Json -Depth 8 | Tee-Object -FilePath "$w\result-$Version.json"
