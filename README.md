# Tree Generator (Unity)

Proceduralny generator drzew 3D do Unity.

Narzędzie tworzy siatkowe drzewo (pień, gałęzie i liście) na podstawie zestawu parametrów zapisanych w `TreeData` (`ScriptableObject`). Generator działa w edytorze, pozwala szybko iterować kształt drzewa i automatycznie przygotowuje poziomy LOD.

## Najważniejsze funkcje

- Proceduralne generowanie pnia, głównych gałęzi, gałązek i liści.
- Konfiguracja przez `TreeData` (seed, krzywe, kąty, rozkład, rozmiary).
- Osobne submeshe dla:
  - drewna (pień + gałęzie),
  - liści,
  - liści zgrupowanych (dla LOD).
- Automatyczna generacja LOD (`LODGroup`) z redukcją geometrii i liści.
- Opcjonalne grupowanie wewnętrznych liści na dalszych LOD.
- Automatyczne/ ręczne sterowanie `Scale In Lightmap` dla poziomów LOD.
- Przyciski edytorowe:
  - `Generuj Drzewo`
  - `Generuj Lightmap UV (UV2)`
- Walidacja ustawień i panel statystyk (wierzchołki, trójkąty, liście, LOD).

## Struktura projektu (istotne pliki)

- `Assets/TreeGenerator/TreeGenerator.cs` - główny komponent generujący mesh drzewa.
- `Assets/TreeGenerator/TreeData.cs` - definicja danych i wszystkich sekcji parametrów.
- `Assets/TreeGenerator/Editor/TreeGeneratorEditor.cs` - niestandardowy inspector komponentu.
- `Assets/TreeGenerator/Editor/TreeDataEditor.cs` - niestandardowy inspector assetu `TreeData`.
- `Assets/TreeGenerator/Shaders/LeafBakedLit.shader` - shader liści.

## Wymagania

- Unity (projekt oparty o URP).
- Scena z obiektem posiadającym:
  - `MeshFilter`
  - `MeshRenderer`
  - `TreeGenerator`

## Szybki start

1. Otwórz projekt w Unity.
2. Utwórz asset danych:
   - `Create -> Tree Generator -> Tree Data`
3. Dodaj komponent `TreeGenerator` do obiektu w scenie (lub użyj istniejącego).
4. Przypisz utworzony `TreeData` do pola **Tree Data**.
5. Przypisz materiały:
   - **Materiał drzewa** (submesh 0),
   - **Materiał liści** (submesh 1),
   - opcjonalnie **Materiał grup liści** (submesh 2).
6. Kliknij **Generuj Drzewo**.
7. (Opcjonalnie) kliknij **Generuj Lightmap UV (UV2)** przed bake oświetlenia.

## Jak to działa

- Kształt pnia i gałęzi jest budowany jako zestaw pierścieni (tube mesh) interpolowanych po krzywych.
- Rozmieszczenie gałęzi i liści jest kontrolowane przez seed i krzywe gęstości.
- Liście generowane są jako lekkie prymitywy (LOD0 bardziej szczegółowe, dalsze LOD uproszczone).
- Dla LOD generator:
  - zmniejsza segmentację geometrii,
  - redukuje liczbę liści,
  - kompensuje wizualnie rozmiar liści,
  - opcjonalnie grupuje liście wewnątrz korony.

## Sekcje parametrów `TreeData`

- **Losowość (seed)** - zmiana wariantu drzewa przy tych samych ustawieniach.
- **Pień** - wysokość, promień, segmenty, boki, wygięcie i skręcenie.
- **Główne gałęzie** - liczba, zakres wysokości, długości, kąty i geometria.
- **Gałązki (poziom 1 i 2)** - dalsze rozgałęzienia i ich proporcje.
- **Liście** - gęstość, pozycja, separacja, rozmiar, orientacja i kolor wierzchołka.

## Uwagi

- Generator działa również w trybie edycji (`ExecuteInEditMode`).
- Przy dużych drzewach rośnie liczba wierzchołków i czas generacji - warto korzystać z LOD.
- Dla poprawnego renderowania liści używaj materiałów/shaderów dostosowanych do cienkich powierzchni (leaf cards).

## Licencja

Brak zdefiniowanej licencji w repozytorium. Jeśli planujesz publikację lub użycie komercyjne, dodaj plik `LICENSE`.

