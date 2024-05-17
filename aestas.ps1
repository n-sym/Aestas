#!/usr/bin/pwsh
$templates = @{
ernie = '{
    "api_key": "",
    "secret_key": "",
    "system": "",
    "database": [
        {
            "time": ""
        }
    ],
    "max_length": 4000
}'
cohere = '{
    "api_key": "",
    "system": "",
    "documents": [
        {
            "time": ""
        }
    ],
    "connectors": [
        {"id": "web-search"}
    ],
    "max_length": 4000
}'
gemini = '{
    "api_key": "",
    "gcloudpath" : "",
    "system": "",
    "safetySettings": [
        {
            "category": "HARM_CATEGORY_HARASSMENT",
            "threshold": "BLOCK_MEDIUM_AND_ABOVE"
        },
        {
            "category": "HARM_CATEGORY_HATE_SPEECH",
            "threshold": "BLOCK_MEDIUM_AND_ABOVE"
        },
        {
            "category": "HARM_CATEGORY_SEXUALLY_EXPLICIT",
            "threshold": "BLOCK_MEDIUM_AND_ABOVE"
        },
        {
            "category": "HARM_CATEGORY_DANGEROUS_CONTENT",
            "threshold": "BLOCK_MEDIUM_AND_ABOVE"
        }
    ],
    "database": [
        {
            "time": ""
        }
    ],
    "max_length": 4000
}'
mstts = '{
    "subscriptionKey": "",
    "subscriptionRegion": "",
    "voiceName":"",
    "outputFormat": "amr-wb-16000hz"
}'
fuyu = '{
    "api_key": "",
    "secret_key": ""
}'
awakeme = '{
    "template": 0.0
}'
stickers = '{
    "from_file": {},
    "from_url": {},
    "from_market": {}
    }
}'
}

function Init {
    $path = 'profile/'
    if (!(Test-Path $path)) {
        New-Item -ItemType Directory -Path $path
    }
    foreach ($file in $templates.Keys) {
        $file_path = "$path$file.json"
        if (Test-Path $file_path){
            Move-Item $file_path "$file_path.bak" -Force
        }
        if (!(Test-Path $file_path)) {
            $templates[$file] | Out-File $file_path
        }
    }
    Write-Output "Initialization complete."
}
$usage = "Usage: aestas.ps1 [ build | run | cli | init ]"
if ($args.Length -ne 1) {
    Write-Output $usage
}
else {
    if ($args[0] -eq "build") {
        dotnet build --configuration Release
    }
    elseif ($args[0] -eq "run") {
        dotnet build --configuration Release
        dotnet bin/Release/net8.0/aestas.dll run
    }
    elseif ($args[0] -eq "cli") {
        dotnet build --configuration Release
        dotnet bin/Release/net8.0/aestas.dll cli
    }
    elseif ($args[0] -eq "init") {
        Init
    }
    else {
        Write-Output $usage
    }
}