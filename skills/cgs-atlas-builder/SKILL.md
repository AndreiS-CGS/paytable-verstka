# cgs-atlas-builder

Собирает Sprite Atlas и TMP Sprite Asset из вручную подготовленной папки с PNG-спрайтами для паytable в Unity (CGS/Konami).

**Часть репозитория `paytable-verstka`** — Python-скрипты лежат в `scripts/` рядом с этим файлом,
Unity-side шаги делегированы в C#-утилиту `CGS.PaytableLibrary.PaytableAtlasBuilder`, которая живёт
в `library/Editor/PaytableAtlasBuilder.cs` (та же библиотека, что использует `paytable-verstka`).
Ничего из этого не переписывается заново каждый запуск — вызываются готовые функции.

## Когда использовать

Когда юзер хочет запаковать собранный арт символов в TMP Sprite Asset для паytable.
Триггеры: "запакуй атлас", "собери спрайт ассет", "cgs atlas", "паytable атлас".

## Контекст проекта (портируемо — без хардкода путей)

- Unity проект: **определять в рантайме** (искать корень `Konami-Slots` / git-корень), не хардкодить
  `/Users/<name>/…`.
- Бандлы: GEL — `Assets/Bundles/_gel/_games/<slot>/`; MCF («обычные») — `Assets/Bundles/_games/<slot>/`.
- Staging папка: `sprites/_PaytableAtlas/` внутри бандла (PNG кладутся туда).
- Имена символов: из рабочих артефактов вёрстки (`_verstka/<Slot> Symbols.md` / `blocks.yaml`), НЕ из
  персонального Obsidian-вола. Формат `[SYMBOL_NAME]` / `<SYMBOL>`.
- **ВАЖНО:** в атлас включать ВСЕ символы — и гридовые (PIC/cards), и те, что встречаются в ТЕКСТЕ
  правил (иначе TMP подставит дефолтный эмодзи вместо `<sprite name="X">`).

## Входные данные

Спросить у юзера (если не указано):
1. **Путь к бандлу** (относительно `Assets/`) — например `Bundles/_gel/_games/crazystuffedcoinsgoat`
2. **Имя слота** для нейминга — например `Goat` → `Goat_PaytableAtlasTex.png`, `Goat_PaytableSpriteAsset.asset`

## Шаги выполнения

### 1. Обработка PNG (Python — `scripts/process_pngs.py`)

```bash
python3 "<skill_dir>/scripts/process_pngs.py" "<путь к исходным PNG>" \
  --height 128 --small-height 100 --small-height-names grand,major,mini,minor
```
Обрезает по альфе (порог 127 по умолчанию), ресайзит пропорционально до указанной высоты, пишет в
`<src>_128/`. `--small-height-names` — список имён (lowercase), которым нужна меньшая высота
(обычно джекпот-бейджи).

### 2. Паковка в атлас (Python — `scripts/pack_atlas.py`)

```bash
python3 "<skill_dir>/scripts/pack_atlas.py" "<src>_128" \
  "<бандл>/<Slot>_PaytableAtlasTex.png" "<бандл>/<Slot>_PaytableAtlasTex.json" \
  --atlas-size 1024 --pad 4
```
Пакует все PNG из шага 1 в один атлас + JSON с координатами (уже во Unity-конвенции — Y от низа
текстуры). Бросает ошибку при переполнении 1024×1024 — в этом случае уменьшить высоту в шаге 1 и
повторить оба шага. JSON хранить рядом с PNG в бандле (пригодится и позже, хотя шаг 6 может читать
его напрямую).

### 3. Копирование в Unity staging

```bash
STAGING="<абс. путь к бандлу>/sprites/_PaytableAtlas"
mkdir -p "$STAGING"
cp "<src>_128"/*.png "$STAGING/"
```

### 4-7b. Unity-side шаги — через `PaytableAtlasBuilder` (C#, `library/Editor/PaytableAtlasBuilder.cs`)

Все шаги внутри Unity делегированы готовым статическим методам — вызываются через unityMCP
`execute_code`, не переписываются вручную:

```csharp
using CGS.PaytableLibrary;

string bundle = "<абс. путь к бандлу>";
string slot = "<Slot>";
string assetPath = bundle + $"/{slot}_PaytableSpriteAsset.asset";
string texPath   = bundle + $"/{slot}_PaytableAtlasTex.png";
string jsonPath  = bundle + $"/{slot}_PaytableAtlasTex.json";
string matPath   = bundle + $"/{slot}_PaytableSpriteAsset Material.mat";

// Step 4 — material (once per new sprite asset)
var mat = PaytableAtlasBuilder.CreateSpriteMaterial(matPath);

// (создание самого .asset с пустыми m_SpriteGlyphTable: [] / m_SpriteCharacterTable: []
//  и Face Info — см. "Face Info" ниже — делается один раз при первом создании ассета)

// Step 5 — hashes, ТОЛЬКО из TMP_TextUtilities, никогда наивной формулой
var sprites = PaytableAtlasBuilder.ReadSpriteRects(jsonPath);
var names = System.Array.ConvertAll(sprites, s => s.name);
var hashes = PaytableAtlasBuilder.GetHashCodes(names);

// Step 6 — write tables directly into the YAML (C# SerializedObject does NOT persist lists here)
PaytableAtlasBuilder.WriteSpriteAssetTables(assetPath, sprites, hashes);

// Step 7 — final import + mandatory 4-point verification (throws on failure)
string report = PaytableAtlasBuilder.FinalImportAndVerify(assetPath, texPath, sampleSymbolName: names[0]);

// Step 7b — slice the SAME atlas into Sprite sub-assets for hero symbols used as plain Image
int sliced = PaytableAtlasBuilder.SliceIntoSubSprites(texPath, assetPath);

return report + $" | sliced {sliced} sub-sprites";
```

**Face Info** (через `SerializedObject`, только сразу после `AssetDatabase.CreateAsset` на новом
ассете — до этого `WriteSpriteAssetTables` не трогает Face Info, так что это отдельный шаг):
```csharp
var so = new UnityEditor.SerializedObject(sa);
so.FindProperty("material").objectReferenceValue = mat;
so.FindProperty("m_FaceInfo.m_PointSize").intValue     = 128;
so.FindProperty("m_FaceInfo.m_Scale").floatValue       = 1.0f;
so.FindProperty("m_FaceInfo.m_LineHeight").floatValue  = 128.0f;
so.FindProperty("m_FaceInfo.m_AscentLine").floatValue  = 128.0f;
so.ApplyModifiedProperties();
UnityEditor.AssetDatabase.SaveAssets();
```

### 8. Результат

```
✅ <Slot>_PaytableAtlasTex.png   — 1024×1024 атлас
✅ <Slot>_PaytableAtlasTex.json  — координаты (хранить в бандле)
✅ <Slot>_PaytableSpriteAsset Material.mat
✅ <Slot>_PaytableSpriteAsset.asset — N спрайтов + N суб-спрайтов (нарезка шага 7b)
```

Использование в TMP тексте: `<sprite name="SYMBOL_NAME">`. Использование как обычной картинки:
`Image.sprite` = один из суб-спрайтов той же текстуры (см. `paytable-verstka` BLOCKS.md — "символ =
Image vs инлайн-спрайт").

## Известные грабли

| Проблема | Решение |
|---|---|
| Спрайты не рендерятся по имени | Хеши неправильные — брать ТОЛЬКО из `PaytableAtlasBuilder.GetHashCodes` (шаг 5) |
| Таблицы пустые в инспекторе | Не использовать C# API, `WriteSpriteAssetTables` пишет YAML напрямую (шаг 6) |
| Default Material: None | `PaytableAtlasBuilder.CreateSpriteMaterial` — шейдер TextMeshPro/Sprite (шаг 4) |
| Атлас не влезает в 1024 | `pack_atlas.py` бросит ошибку сам — уменьшить высоту в `process_pngs.py` (grand/major/mini/minor → 100px) и повторить оба шага |
| `<sprite name="X">` → literal text | Неправильные хеши ИЛИ не вызван `UpdateLookupTables()` (входит в `FinalImportAndVerify`) |
| `<sprite name="X">` → ПУСТО (таблицы/хеши ок) | Атлас-текстура не назначена — `FinalImportAndVerify` бросит исключение с отчётом, что именно не так |
| SerializedObject не сохраняет списки | Баг Unity — `WriteSpriteAssetTables` пишет YAML напрямую, это обходит проблему |
| Инлайн-спрайты мыльные / нечёткие | character `m_Scale = 30` (спрайты 128px рендерятся ×2 → чётче). Зависимость размера: `meshH ≈ 0.1 × m_Scale × fontSize` → для эталонного размера `fontSize ≈ 1860/m_Scale` (62 при scale 30), `voffset ≈ 9.7×fontSize` (600). См. `paytable-verstka` `library/BLOCKS.md` |
| Символ-герой нужен как обычный `Image`, а не инлайн-тег | `PaytableAtlasBuilder.SliceIntoSubSprites` (шаг 7b) — нарезает тот же атлас по тем же rect'ам из `spriteGlyphTable`, без дублирования текстуры |
