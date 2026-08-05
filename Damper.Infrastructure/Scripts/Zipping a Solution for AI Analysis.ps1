# from your solution root
$exclude = @('bin','obj','.vs','.git')
robocopy . ..\damper-mvp-temp /E /XD $exclude
Compress-Archive -Path ..\damper-mvp-temp\* -DestinationPath .\damper-mvp.zip
Remove-Item -Recurse -Force ..\damper-mvp-temp