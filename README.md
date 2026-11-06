# CS2 Kit Builder

Personal, free, non-commercial app that **builds Counter-Strike 2 skin kits from a budget and a color**:
you say how much you want to spend, which color you want and which items the kit needs; the app
assembles the whole kit, crossing color, price, rarity and category, with cheaper and pricier
alternatives for each piece.

Status: phases 0–2 (foundation, data, kit engine) in progress — the Windows UI (WinUI 3) is phase 3.

## Repository

| Folder | Content |
|---|---|
| `windows/` | .NET 10 solution: `KitBuilder.Core` (domain + kit engine), `KitBuilder.Data` (catalog, Steam prices, inventory, cache), tests, tools |
| `data-pipeline/` | Color index generator (OKLab k-means over every skin image) |
| `shared/` | Design tokens, cross-platform test fixtures, legal texts |
| `docs/` | `data-sources.md` (verified endpoints), `kit-engine.md` (engine spec), `architecture.md`, `release.md` |

## Developer commands

```bash
dotnet test windows/KitBuilder.slnx
python -m unittest discover -s data-pipeline/tests
python data-pipeline/build_color_index.py --out out/color-index.json
dotnet run --project windows/tools/KitBuilder.EndpointProbe -- --steam-id <profile URL>
dotnet run --project windows/tools/KitBuilder.Cli -- prices
dotnet run --project windows/tools/KitBuilder.Cli -- kit --budget 200 --color "#d32f2f" --slots ak47,m4,awp,deagle,pistol --colors out/color-index.json
```

## Aviso legal

CS2 Kit Builder é um projeto independente e sem fins comerciais. Não é afiliado, patrocinado nem endossado pela Valve Corporation. Counter-Strike, Counter-Strike 2, Steam, os logotipos e as imagens dos itens são marcas e/ou propriedade da Valve Corporation. Todos os direitos sobre esses itens pertencem aos seus respectivos titulares. Preços são estimativas obtidas do Mercado da Comunidade Steam e podem estar desatualizados.

## License

[PolyForm Noncommercial 1.0.0](LICENSE). Third-party licenses: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
Item catalog by [ByMykel/CSGO-API](https://github.com/ByMykel/CSGO-API) (MIT).
