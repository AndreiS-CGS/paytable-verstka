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
1. **Путь к бандлу** (относительно `Assets/`) — вида `Bundles/_gel/_games/<slot>`
2. **Имя слота** для нейминга — `<Slot>` → `<Slot>_PaytableAtlasTex.png`, `<Slot>_PaytableSpriteAsset.asset`

## Шаги выполнения

### 1. Обработка PNG (Python — `scripts/process_pngs.py`)

```bash
python3 "<skill_dir>/scripts/process_pngs.py" "<путь к исходным PNG>" --height 128
```
Обрезает по альфе (порог 127 по умолчанию), ресайзит пропорционально до указанной высоты, пишет в
`<src>_128/`.

**Высота всегда 128 у всех спрайтов без исключений, ширина свободная.** Именно это делает шрифт
стандартным: `glyph.height = pointSize`, и формулы из «Формул» сворачиваются в
`spriteHeight = fontSize × P/100`. Никакого второго тира для джекпот-бейджей — нужный размер в
каждом месте даёт тег `<size=P%>`, а не отдельная высота в атласе. Флаги `--small-height` /
`--small-height-names` относились к прежней схеме и больше не используются.

### 2. Паковка в атлас (Python — `scripts/pack_atlas.py`)

```bash
python3 "<skill_dir>/scripts/pack_atlas.py" "<src>_128" \
  "<бандл>/<Slot>_PaytableAtlasTex.png" "<бандл>/<Slot>_PaytableAtlasTex.json" \
  --pow2 --atlas-size 4096 --pad 4
```
Пакует все PNG из шага 1 в один атлас + JSON с координатами (уже во Unity-конвенции — Y от низа
текстуры). JSON хранить рядом с PNG в бандле (пригодится и позже, хотя шаг 6 может читать его
напрямую).

**Размер не подбирать руками — передать `--pow2`.** Скрипт сам найдёт минимальную подходящую
степень двойки по ширине и отдельно ужмёт высоту. При `--pow2` аргумент `--atlas-size` работает как
**потолок**, а не как заданный размер, поэтому его можно ставить с запасом.

Высоту у спрайтов **не уменьшать ради влезания** — 128px это часть стандарта шрифта, и любое
отклонение ломает `spriteHeight = fontSize × P/100`. Если контент не влезает даже в потолок —
поднимать потолок.

Укладка рядами («полками»), и это near-optimal именно здесь: по стандарту все спрайты одной высоты,
поэтому ряды выходят ровные и пустым остаётся только хвост последнего. Более умные упаковщики
(MaxRects и родственники) на однородном входе отыгрывают единицы процентов и не стоят усложнения.

Отдельная высота даёт заметную экономию: ряды однородные, контент обычно намного шире, чем выше, и
квадратный атлас потратил бы половину текстуры впустую.

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

**Face Info — стандартная конфигурация.** Задаётся через `SerializedObject`, только сразу после
`AssetDatabase.CreateAsset` на новом ассете (до этого `WriteSpriteAssetTables` не трогает Face Info,
так что это отдельный шаг).

Смысл стандарта: шрифт настраивается **один раз и одинаково для всех слотов**, а нужный размер
спрайта в каждом месте задаётся тегом `<size=P%>`, а не правкой ассета. Все значения ниже выведены
из исходников TMP (см. «Формулы»), а не подобраны.

> **Стандарт применяется к новым ассетам. Уже собранные не миграционить.**
> Ассет, собранный по другой схеме, выглядит правильно ровно потому, что его тексты подогнаны под
> его собственные параметры. Перевод такого ассета на стандарт без одновременной правки всех его
> текстов изменит рендер уже отгруженной игры.

```csharp
var so = new UnityEditor.SerializedObject(sa);
so.FindProperty("material").objectReferenceValue = mat;
so.FindProperty("m_FaceInfo.m_PointSize").intValue     = 128;   // = высота исходников
so.FindProperty("m_FaceInfo.m_Scale").floatValue       = 10.0f; // = 1 / orthoMult, см. Формулы
so.FindProperty("m_FaceInfo.m_LineHeight").floatValue  = 128.0f;
so.FindProperty("m_FaceInfo.m_AscentLine").floatValue  = 64.0f; // половина высоты
so.FindProperty("m_FaceInfo.m_Baseline").floatValue    = 0.0f;
so.FindProperty("m_FaceInfo.m_DescentLine").floatValue = -64.0f;
so.ApplyModifiedProperties();
UnityEditor.AssetDatabase.SaveAssets();
```

Метрики глифов (пишет `WriteSpriteAssetTables`): `height = 128`, `width` = фактическая,
`horizontalAdvance = width`, **`horizontalBearingY = 64`**, `glyph.scale = 1`,
`character.m_Scale = 1`.

`bearingY = 64` — половина высоты глифа, то есть спрайт центрируется **по базовой линии**. Это и
есть причина выбрать 64, а не «правильную» точку центра по cap: при любом другом значении
`bearingY` пришлось бы пересчитывать под каждый `P`, а при 64 поправка на cap становится одной
константой на шрифт, от `P` не зависящей.

### Формулы

Выведены из `com.unity.ugui/Runtime/TMP/TextMeshPro.cs` — строки 2227 (`orthographicMultiplier`),
2552–2559 (масштаб спрайта), 2836/2841 (вершины квада). Не приблизительные.

```
orthoMult    = m_isOrthographic ? 1 : 0.1
spriteScale  = fontSize / faceInfo.pointSize × faceInfo.scale × orthoMult
elementScale = character.m_Scale × glyph.scale × spriteScale

spriteHeight     = glyph.height   × elementScale
topAboveBaseline = glyph.bearingY × elementScale
```

При стандартной конфигурации (`glyph.height = pointSize = 128`, оба scale = 1) это сворачивается в:

```
spriteHeight = fontSize × faceInfo.scale × orthoMult × P/100
```

и при `faceInfo.scale = 1 / orthoMult`:

```
spriteHeight = fontSize × P/100
```

**То есть `P` из тега `<size=P%>` — это прямо высота спрайта в процентах от кегля текста.**
`P = 100` → спрайт ростом в кегль; `P = 250` → в 2.5 кегля.

### Готовые значения P

Инлайн-спрайт **никогда не ставится без тега размера.** Без него `P = 100`, то есть спрайт выходит
ростом ровно в прописную букву — визуально это втрое-впятеро мельче, чем в референсах.

Замер по референсным рендерам (высота прописной 11px) даёт два устойчивых класса:

| Класс | Высота в референсе | Целевое R (в cap) | **P при кегле 32** |
|---|---|---|---|
| Арт символа (обычный инлайн) | 48–60px | **5.0×** | **500%** |
| Бейдж/лого джекпота (широкий, плоский) | 24px | **2.2×** | **225%** |

Высота внутри класса держится независимо от пропорции арта: узкий высокий динамит и широкий низкий
знак рендерятся одной высоты. Кажущаяся разница в размере между ними — это пропорции самого арта, а
не разный тег. Разными тегами отличаются только два класса выше.

Общая формула, если кегль или шрифт другие:

```
P = 100 × R × capLine_em
```

где `R` — желаемая высота в высотах прописной, а `capLine_em` — высота прописной в em у того шрифта,
которым набран текст (см. «Вертикальная поправка»). При `capLine_em ≈ 1.02`: `R = 5` → `P ≈ 510`,
`R = 2.2` → `P ≈ 224`.

Готовый вид тега:

```
<size=500%><voffset=0.51em><sprite name="SYMBOL"></voffset></size>
```

**Вертикальная поправка** — одна константа на шрифт, не зависит от `P`:

```
voffset = capLine_em / 2
capLine_em = font.capLine × font.faceInfo.scale × orthoMult / font.faceInfo.pointSize
```

Считается один раз для того font asset'а, которым набран Body: взять из его `faceInfo` поля
`capLine`, `scale` и `pointSize`, подставить, получить константу в em. Например при
`capLine 33, scale 13, pointSize 42` и `orthoMult = 0.1` выходит `capLine_em ≈ 1.02` → `voffset ≈ +0.51em`.

Отношение к высоте прописной: `spriteHeight / capH = P / (100 × capLine_em)`.

> **Грабля с `orthoMult`.** Множитель 0.1 — это не константа TMP, а `m_isOrthographic ? 1 : 0.1`.
> Body-тексты паytable — `TextMeshPro` (3D) с `m_isOrthographic: 0`, поэтому 0.1 и
> `faceInfo.scale = 10`. Если текст сделать ортографическим или перевести на `TextMeshProUGUI`,
> множитель станет 1 и **все спрайты вырастут в 10 раз** — тогда `faceInfo.scale` должен быть 1.
> Это единственное место, где конфигурация зависит от типа текстового компонента.

### Имя спрайта из токена GDD

Токены в тексте GDD и имена спрайтов в атласе связаны двумя правилами:

```
' '  ->  '_'
'+'  ->  'PLUS'
```

Например `[+1 SPIN]` → `<sprite name="PLUS1_SPIN">`, `[DARK ACE]` → `DARK_ACE`,
`[MINI BONUS]` → `MINI_BONUS`.

**Сверять в одну сторону: каждый токен обязан иметь спрайт, но не наоборот.** Атлас законно
содержит спрайты, которых в тексте правил нет — гридовые символы (карточные ранги и PIC'и) приходят
из Pay Grid, а не из текста, и в токенах могут не встречаться ни разу.

### 8. Результат

```
✅ <Slot>_PaytableAtlasTex.png   — атлас (размер подобран --pow2, спрайты 128 по высоте)
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
| Атлас не влезает | Поднять потолок `--atlas-size`. **Не уменьшать высоту спрайтов** — 128 это часть стандарта шрифта, любое отклонение ломает формулу `spriteHeight = fontSize × P/100` |
| `<sprite name="X">` → literal text | Неправильные хеши ИЛИ не вызван `UpdateLookupTables()` (входит в `FinalImportAndVerify`) |
| `<sprite name="X">` → ПУСТО (таблицы/хеши ок) | Атлас-текстура не назначена — `FinalImportAndVerify` бросит исключение с отчётом, что именно не так |
| SerializedObject не сохраняет списки | Баг Unity — `WriteSpriteAssetTables` пишет YAML напрямую, это обходит проблему |
| Инлайн-спрайты мыльные / нечёткие | Не трогать ассет — увеличить `P` в теге `<size=P%>` на месте использования. Исходники всегда 128px, поэтому запас разрешения есть; резкость даёт больший `P`, а не правка `m_Scale` |
| Спрайты вдруг стали в 10 раз больше/меньше | Сменился тип текстового компонента или `m_isOrthographic` — поменялся `orthoMult`. См. граблю про `orthoMult` в «Формулах» |
| Спрайт съехал по вертикали | `bearingY` должен быть 64 у всех глифов; вертикаль правится только `voffset` в теге, одной константой на шрифт |
| Символ-герой нужен как обычный `Image`, а не инлайн-тег | `PaytableAtlasBuilder.SliceIntoSubSprites` (шаг 7b) — нарезает тот же атлас по тем же rect'ам из `spriteGlyphTable`, без дублирования текстуры |
