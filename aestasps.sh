#!/bin/bash
#ported by copilot
templates=(
ernie='{
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
cohere='{
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
gemini='{
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
mstts='{
    "subscriptionKey": "",
    "subscriptionRegion": "",
    "voiceName":"",
    "outputFormat": "amr-wb-16000hz"
}'
fuyu='{
    "api_key": "",
    "secret_key": ""
}'
awakeme='{
    "template": 0.0
}'
stickers='{
    "from_file": {},
    "from_url": {},
    "from_market": {}
}'
)

function Init {
    path="profile/"
    if [[ ! -d $path ]]; then
        mkdir -p $path
    fi
    for file in "${!templates[@]}"; do
        file_path="$path$file.json"
        if [[ -f $file_path ]]; then
            mv $file_path "$file_path.bak"
        fi
        if [[ ! -f $file_path ]]; then
            echo "${templates[$file]}" > $file_path
        fi
    done
    echo "Initialization complete."
}

if [[ $# -ne 1 ]]; then
    echo "Usage: aestas.sh [ build | run | cli | init ]"
else
    if [[ $1 == "build" ]]; then
        dotnet build --configuration Release
    elif [[ $1 == "run" ]]; then
        dotnet build --configuration Release
        dotnet bin/Release/net8.0/aestas.dll run
    elif [[ $1 == "cli" ]]; then
        dotnet build --configuration Release
        dotnet bin/Release/net8.0/aestas.dll cli
    elif [[ $1 == "init" ]]; then
        Init
    else
        echo "Usage: aestas.sh [ build | run | cli | init ]"
    fi
fi
