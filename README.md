# AIDataPlatform

It was once a working project. It was on standby for a while.

If you wanna help me build it with **MudBlazor**, **Syncfusion** components, and my other **Python multi-agent backend**, let's do it.

## Features

There are many cool features like:

- File uploads
- File sharing from Azure (I don't wanna use Azure at all anymore)
- Vertex coordinates overlay on OCR-extracted PDF files
- A PDF viewer
- File manager to see your files and folders (worked with Azure Blob containers — needs an alternative)
- A chat component
- Services to call Python FastAPI backend APIs
- Used to store extracted OCR metadata on Azure Cosmos DB (don't wanna use Azure anymore)

## Cool features

- EF Core is a core part of the project
- Supports multi-tenancy (sharding) with `TenantId`

## Todo's

- Make it work again :-P
- Show examples of how it can work with Docker containers
- Instead of Cosmos DB, use Postgres with Docker
- Instead of Azure Blob containers, use Nextcloud with Docker or simply the local file system inside a container
- Rewrite Blob Storage services to work with another file system architecture (obviously)
- Upgrade from .NET 9 to .NET 11
- Update all component libraries like MudBlazor and Syncfusion

## Configuration

This repo does **not** contain any secrets — app settings with credentials are git-ignored.

To run locally, copy the example settings file and fill in your own values:

```bash
cp AIDataPlatform/appsettings.Development.example.json AIDataPlatform/appsettings.Development.json
```

Then edit `appsettings.Development.json` with your own connection strings and API keys.
