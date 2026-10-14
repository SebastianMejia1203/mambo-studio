$ErrorActionPreference = 'Stop'
$target = Join-Path $PSScriptRoot '../Assets/Icons/Lucide'
New-Item -ItemType Directory -Path $target -Force | Out-Null
$icons = @('plus','layout-grid','list','camera','mic','file-plus-2','folder-open','save','import','upload','mouse-pointer-2','crop','split','scissors','copy','clipboard-paste','trash-2','undo-2','redo-2','panel-left-close','panel-right-close','rotate-ccw','type','music','video','image','maximize','zoom-in','zoom-out','play','pause','square','circle','skip-back','skip-forward','rewind','fast-forward','chevron-left','chevron-right','x','settings-2','check','files','sliders-horizontal','palette','eye','magnet','panel-left','panel-right','arrow-left-right','ellipsis','search','image-plus')
$icons | ForEach-Object -Parallel {
    Invoke-WebRequest -Uri "https://raw.githubusercontent.com/lucide-icons/lucide/0.468.0/icons/$_.svg" -OutFile (Join-Path $using:target "$_.svg")
} -ThrottleLimit 6
Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/lucide-icons/lucide/0.468.0/LICENSE' -OutFile (Join-Path $target 'LICENSE')
"Downloaded $($icons.Count) Lucide icons (0.468.0)."
