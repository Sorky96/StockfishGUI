# Plan wdrożenia: indywidualny analizator błędów szachowych

## Cel projektu

Zbudować system, który:
- importuje partie PGN,
- analizuje ruchy gracza z użyciem Stockfisha,
- wykrywa błędy i klasyfikuje ich typ,
- tłumaczy błędy prostym językiem,
- buduje profil najczęściej popełnianych błędów,
- z czasem daje coraz bardziej spersonalizowane wskazówki.

To nie jest wersja "pełne własne AI od zera", tylko dobra i trudniejsza wersja produktu:
- silnik szachowy odpowiada za prawdę szachową,
- warstwa heurystyk i klasyfikacji odpowiada za wykrywanie rodzaju błędu,
- lokalna warstwa generatora porad odpowiada za tłumaczenie błędów ludzkim językiem.

---

## Założenia projektowe

### Co ma robić system
- wskazać, które ruchy były słabe,
- wyjaśnić, dlaczego były słabe,
- wskazać lepszą alternatywę,
- przypisać błąd do kategorii,
- agregować błędy na poziomie jednej partii i wielu partii,
- pokazywać powtarzalne wzorce w grze użytkownika.

### Czego nie robić na start
- nie trenować własnego modelu od zera,
- nie budować własnego silnika szachowego,
- nie próbować od razu rozpoznawać wszystkich niuansów strategicznych,
- nie generować zbyt długich komentarzy,
- nie opierać całego systemu wyłącznie na jednej liczbie eval.

---

## Docelowa architektura

## Status repo po wdrożeniu MVP v1

Poniższy plan został częściowo wdrożony w aktualnym repo. To oznacza, że dalsze prace powinny być kontynuacją istniejącego pipeline, a nie budową od zera.

### Co jest już gotowe
- [x] Import PGN do aplikacji.
- [x] Parser SAN / PGN oraz replay partii ruch po ruchu.
- [x] Generowanie FEN przed i po ruchach.
- [x] Obsługa pełnego round-tripu FEN z `en passant`.
- [x] Replay importowanej partii oparty o `ChessGame` i snapshoty FEN zamiast lokalnego parsera SAN w `MainForm`.
- [x] Walidacja i wykonywanie ręcznych ruchów w UI oparte o `ChessGame` oraz legalne ruchy UCI.
- [x] Strukturalne API analizy silnika oparte o Stockfisha i UCI.
- [x] Analiza jednej partii dla wybranego koloru.
- [x] Wyliczanie `best move`, `eval before`, `eval after`, `centipawn loss`.
- [x] Podstawowy podział faz partii: opening / middlegame / endgame.
- [x] Selekcja najważniejszych błędów partii.
- [x] Heurystyczny klasyfikator v1.
- [x] Rozszerzone heurystyki debiutowe o rozwój figur lekkich i nierozwijające ruchy w otwarciu.
- [x] Rozszerzone heurystyki końcówkowe o centralizację króla i wykrywanie przegapionej aktywizacji króla.
- [x] Szablonowy generator krótkich wyjaśnień.
- [x] Wydzielony kontrakt `IAdviceGenerator` z implementacją `TemplateAdviceGenerator` jako przygotowanie pod przyszły mocniejszy generator lokalny.
- [x] Rozszerzony `AdviceGenerationContext` i `AdvicePromptContextBuilder`, które budują ustrukturyzowany lokalny payload pod przyszły model on-device.
- [x] Parser odpowiedzi lokalnego modelu (`LocalModelAdviceResponseParser`) dla `JSON` i formatu pól `short_text` / `detailed_text` / `training_hint`.
- [x] Rozszerzony komentarz edukacyjny w oknie analizy.
- [x] Integracja z UI: po imporcie PGN można uruchomić analizę importowanej partii.
- [x] Integracja z UI: wybór błędu przenosi planszę do właściwej pozycji i pokazuje zagrany ruch oraz `best move`.
- [x] Integracja z UI: filtry jakości ruchu, poprawione numerowanie ruchów oraz współrzędne planszy.
- [x] Cache całego wyniku analizy partii w sesji aplikacji.
- [x] Trwały cache analizy jednej partii w SQLite między uruchomieniami aplikacji.
- [x] Przywracanie ostatniego stanu okna analizy po ponownym otwarciu dialogu.
- [x] Zapamiętywanie wybranego poziomu wyjaśnień w stanie okna analizy.
- [x] Biblioteka zapisanych partii z filtrowaniem i ponownym wczytaniem do głównej planszy.
- [x] Odchudzenie UI: `Form1` przemianowany na `MainForm`, plansza wydzielona do `ChessBoardControl`, a stan zaimportowanej partii do osobnej sesji `ImportedGameSession`.
- [x] Odchudzenie UI: replay i nawigacja po zaimportowanej partii wydzielone z `MainForm` do koordynatora `ImportedGamePlaybackCoordinator`.
- [x] Odchudzenie UI: runtime trackingu wydzielony z `MainForm` do koordynatora `TrackingWorkflowCoordinator`.
- [x] Odchudzenie UI: analiza opcji ruchu wybranej figury wydzielona z `MainForm` do `PieceMoveOptionsCoordinator`.
- [x] Odchudzenie UI: nawigacja po wynikach analizy i wspólne formatowanie ruchów wydzielone z `MainForm` do `AnalysisNavigationCoordinator` i `ChessMoveDisplayHelper`.
- [x] Odchudzenie UI: ręczna interakcja z planszą i selekcja figur wydzielone z `MainForm` do `BoardInteractionCoordinator`.
- [x] Odchudzenie UI: prezentacja planszy, oceny i sugestii silnika wydzielona z `MainForm` do `BoardPresentationCoordinator`.
- [x] Podstawowy `PlayerProfileService` agregujący zapisane analizy wielu partii.
- [x] Prosty widok profilu gracza z top kategoriami błędów, fazami, otwarciami i trendem miesięcznym.
- [x] Podstawowe rekomendacje treningowe z priorytetami, checklistą i sugerowanymi ćwiczeniami.
- [x] Testy jednostkowe, integracyjne i end-to-end dla MVP jednej partii.

### Co jest świadomie jeszcze poza MVP
- [x] zapis wyników analizy do SQLite, w tym trwały cache pełnego `GameAnalysisResult` oraz strukturalny zapis `MoveAnalysis`,
- [x] historia analiz i odczyt wcześniejszych partii,
- [x] bardziej zaawansowany profil gracza z wielu partii, oparty przede wszystkim o strukturalny zapis `analysis_moves` z fallbackiem do starszych zapisów pełnych wyników,
- [x] bardziej adaptacyjne rekomendacje treningowe oparte o dane historyczne,
- [x] opcjonalny mocniejszy generator lokalny on-device,
- [x] adapter uruchamiający lokalny model na bazie istniejącego `AdvicePromptContext`, z lokalnym fallbackiem heurystycznym (`LocalModelAdviceGenerator`),
- [x] parser surowej odpowiedzi modelu lokalnego i walidacja kompletności pól przed fallbackiem,
- [x] generyczny adapter procesowy uruchamiający lokalny model przez `stdin/stdout` (`LocalProcessAdviceModel`) konfigurowany lokalnie bez zależności od zewnętrznego API,
- [x] oficjalnie wspierana ścieżka `llama.cpp` z wykrywaniem `llama-cli.exe` i kontrolowanym modelem `stockifhsgui-advice*.gguf`,
- [x] ograniczenie odpowiedzi `llama.cpp` przez lokalną gramatykę JSON, żeby model zwracał pola `short_text`, `detailed_text`, `training_hint`,
- [x] status gotowości lokalnego modelu i smoke test `llama.cpp` w oknie analizy, tak żeby użytkownik mógł sprawdzić runtime bez restartu aplikacji,
- [x] trwały serwer `llama-server` startowany jako proces potomny aplikacji, z automatycznym portem i health checkiem — model ładowany raz, wiele zapytań HTTP na `127.0.0.1`,
- [x] priorytet runtime: `llama-server` (najszybszy) > `llama-cli` (per-request) > heurystyki lokalne,
- [x] analiza zbiorcza (`CreateBulkAnalysisGenerator`) korzysta z LLM gdy dostępny jest serwer, bo koszt per-request jest niski,
- [ ] dalsze porządkowanie `MainForm`, ale bez lokalnej logiki legal moves / SAN jako źródła prawdy.

### Zasada na dalsze etapy
Nowe funkcje powinny być dokładane do usług domenowych i modeli analitycznych, a nie do `MainForm`. UI ma wywoływać pipeline i prezentować wynik.

## Warstwa 1 - Import i przygotowanie danych
Odpowiedzialność:
- wczytanie PGN,
- parsowanie ruchów,
- identyfikacja analizowanego gracza,
- zapis kolejnych pozycji jako FEN,
- wykrycie fazy partii.

### Wejście
- PGN partii,
- informacja, czy analizujemy białe czy czarne,
- opcjonalnie: rating, czas, źródło partii.

### Wyjście
- lista pozycji ruch po ruchu,
- metadane partii,
- lista półruchów do analizy.

### Zadania
- [x] Dodać parser PGN do aktualnej aplikacji.
- [x] Dla każdego półruchu zapisać:
  - numer ruchu,
  - stronę,
  - FEN przed ruchem,
  - ruch zagrany,
  - SAN/UCI,
  - FEN po ruchu.
- [x] Dodać podstawowy podział na opening / middlegame / endgame.
- [x] Oznaczyć ruchy analizowanego gracza.

### Definicja fazy partii - wersja praktyczna
Na start można użyć prostych reguł:
- **opening**: do momentu rozwinięcia kilku figur i wczesnej fazy gry,
- **middlegame**: większość pozycji po debiucie,
- **endgame**: mało figur ciężkich i uproszczony materiał.

Nie musi być idealnie. Ważne, żeby system umiał grupować błędy według faz gry.

---

## Warstwa 2 - Analiza silnikowa
Odpowiedzialność:
- uruchomienie Stockfisha,
- analiza pozycji przed ruchem,
- pobranie najlepszego ruchu,
- ocena ruchu zagranego przez gracza,
- policzenie straty jakości ruchu.

### Co liczyć dla każdego ruchu gracza
- eval przed ruchem,
- best move,
- opcjonalnie top N ruchów,
- eval po zagraniu ruchu gracza,
- centipawn loss,
- zmiana oceny,
- principal variation.

### Zadania
- [x] Zbudować `StockfishService` oparty o UCI.
- [x] Dodać analizę pozycji przed ruchem gracza.
- [x] Dodać analizę pozycji po ruchu gracza.
- [x] Zaimplementować konfigurowalne depth / movetime.
- [x] Zapisywać PV dla najlepszego ruchu oraz dla ruchu gracza.
- [x] Obsłużyć przypadki mate score i centipawn score.

### Proponowane progi jakości ruchu
- 0-30 cp: ruch bardzo dobry / naturalny
- 31-80 cp: lekka niedokładność
- 81-150 cp: niedokładność
- 151-300 cp: błąd
- 300+ cp: poważny błąd / blunder

To tylko punkt startowy. Progi później warto skalować względem fazy partii i poziomu gracza.

---

## Warstwa 3 - Detekcja kandydatów na błąd
Odpowiedzialność:
- odfiltrowanie ruchów, które warto komentować,
- ograniczenie szumu,
- przygotowanie wejścia do klasyfikacji.

### Po co ten etap
Nie każdy ruch z gorszym evalem zasługuje na osobny komentarz. System powinien umieć wyłuskać:
- kluczowe błędy,
- punkty zwrotne partii,
- błędy powtarzalne,
- sytuacje edukacyjnie wartościowe.

### Zadania
- [x] Odrzucać kosmetyczne różnice eval.
- [x] Wykrywać największe swingi w partii.
- [x] Grupować serie słabszych ruchów w jeden motyw.
- [x] Ograniczyć liczbę komentowanych pozycji na partię.
- [x] Nadać priorytet błędom, które:
  - tracą materiał,
  - prowadzą do mata,
  - psują bezpieczeństwo króla,
  - przegrywają końcówkę,
  - wynikają z powtarzalnego wzorca.

### Reguła praktyczna na start
Komentować:
- wszystkie blundery,
- większość błędów,
- tylko wybrane niedokładności,
- nie komentować ruchów prawie równoważnych.

---

## Warstwa 4 - Klasyfikacja typu błędu
Odpowiedzialność:
- odpowiedzieć na pytanie: **jaki to był błąd?**

To jest kluczowy element wersji "dobra - trudna".

## Proponowane kategorie błędów

### Taktyczne
- przeoczenie widełek,
- przeoczenie związania,
- przeoczenie natarcia z tempem,
- przeoczenie mata lub sieci matowej,
- strata figury przez taktykę,
- przeoczenie uderzenia podwójnego,
- przeoczenie odsłony,
- przeoczenie szpili / skewer,
- przeoczenie obrony przeciwnika.

### Materiałowe
- oddanie pionka bez rekompensaty,
- oddanie figury,
- zły wymiennik,
- zła ofiara,
- nieodebranie darmowego materiału.

### Strategiczne
- pasywna figura,
- zły plan,
- strata tempa,
- zła aktywność figur,
- zły ruch pionem osłabiający pozycję,
- osłabienie pól,
- zła struktura pionowa,
- brak walki o centrum,
- niepotrzebna wymiana.

### Dotyczące króla
- opóźniona roszada,
- otwarcie linii na własnego króla,
- ignorowanie zagrożenia przy królu,
- osłabienie ciemnych / jasnych pól przy królu.

### Debiutowe
- brak rozwoju figur,
- zbyt wiele ruchów jedną figurą,
- zbyt wczesny atak bez przygotowania,
- ignorowanie zasad debiutowych,
- wyjście hetmanem za wcześnie.

### Końcówkowe
- błędna aktywizacja króla,
- zły plan pionkowy,
- przegranie remisu,
- brak techniki realizacji przewagi,
- błędna wymiana do końcówki.

---

## Strategia klasyfikacji

### Etap 1 - heurystyki ręczne
Najpierw budujesz klasyfikator oparty o reguły.

Przykład:
- jeśli po ruchu figura jest atakowana i niebroniona, a eval mocno spada -> `hanging_piece`
- jeśli Stockfish pokazuje wymuszony motyw taktyczny po kilku półruchach -> `tactical_oversight`
- jeśli ruch pionem odsłania króla i rośnie presja przeciwnika -> `king_safety`
- jeśli w debiucie brak rozwoju i kilka ruchów tą samą figurą -> `opening_principles`

Status w repo:
- [x] wdrożone podstawowe etykiety `material_loss`, `hanging_piece`, `missed_tactic`, `king_safety`, `opening_principles`, `endgame_technique`
- [x] wdrożone `confidence` i `evidence`
- [x] rozszerzone heurystyki debiutowe o wykrywanie przegapionej roszady i przegapionego kroku rozwojowego, gdy najlepszy ruch poprawiał rozwój
- [x] poprawiona separacja `hanging_piece` vs `material_loss`, tak żeby bezpośrednio powieszona i tracona figura dostawała bardziej konkretną etykietę
- [ ] rozszerzyć heurystyki o bogatsze cechy pozycji i lepszą separację etykiet

### Etap 2 - klasyfikacja hybrydowa
Łączysz:
- heurystyki,
- cechy pozycji,
- ewentualnie model ML lub lokalny model on-device do doprecyzowania etykiety.

### Etap 3 - ranking pewności
Dla każdej etykiety zapisujesz:
- label,
- confidence,
- evidence.

Przykład:
```json
{
  "label": "hanging_piece",
  "confidence": 0.91,
  "evidence": [
    "piece_became_undefended",
    "opponent_can_capture_for_free",
    "centipawn_loss_420"
  ]
}
```

---

## Warstwa 5 - Ekstrakcja cech pozycji
Odpowiedzialność:
- policzyć cechy, które pozwolą zrozumieć naturę błędu.

## Minimalny zestaw cech

### Cechy silnikowe
- eval before,
- eval after,
- centipawn loss,
- mate score,
- top 3 moves,
- długość PV,
- forcing line tak / nie.

### Cechy materiałowe
- bilans materiału przed i po,
- czy stracono pionka,
- czy stracono figurę,
- czy oddano jakość,
- czy była darmowa figura do wzięcia.

### Cechy taktyczne
- czy zagrana figura po ruchu jest broniona,
- liczba atakujących i broniących kluczowe pole,
- czy po ruchu pojawia się szach,
- czy przeciwnik uzyskuje wymuszoną sekwencję,
- czy powstała związana figura,
- czy figura została przeciążona.

### Cechy strategiczne
- aktywność figur,
- mobilność,
- kontrola centrum,
- otwarte linie,
- słabe pola,
- zdwojone / izolowane / cofnięte piony,
- bezpieczeństwo króla,
- rozwój figur lekkich,
- status roszady.

### Cechy kontekstowe
- faza partii,
- kolor gracza,
- numer ruchu,
- czas na zegarze jeśli dostępny,
- otwarcie / ECO jeśli dostępne,
- rating gracza jeśli dostępny.

---

## Warstwa 6 - Generator wyjaśnień
Odpowiedzialność:
- zamienić analizę techniczną w krótką, trafną i ludzką informację zwrotną.

## Forma komentarza
Każdy komentarz powinien odpowiadać na 4 pytania:
1. Co było złe?
2. Dlaczego to było złe?
3. Co było lepsze?
4. Jak rozpoznawać taki motyw następnym razem?

### Szablon komentarza
- **Błąd:** co się stało
- **Powód:** jaki motyw został przeoczony
- **Lepsza opcja:** najlepszy ruch lub plan
- **Wskazówka:** czego szukać w podobnych pozycjach

### Przykład formatu
> Zagrałeś ruch, który zostawił skoczka bez wystarczającej obrony. Przeciwnik mógł go wygrać prostą sekwencją taktyczną. Lepsze było wycofanie figury lub wcześniejsze wzmocnienie pola. W podobnych pozycjach sprawdzaj, czy figura po ruchu będzie broniona co najmniej tyle razy, ile jest atakowana.

### Zadania
- [x] Zdefiniować szablon odpowiedzi krótkiej.
- [x] Zdefiniować szablon odpowiedzi rozszerzonej.
- [x] Dodać poziomy tłumaczenia dla np. początkującego, średniego i zaawansowanego gracza.
- [x] Dodać maksymalną długość komentarza.
- [x] Dodać sekcję "jak ćwiczyć ten motyw".

---

## Warstwa 7 - Profil gracza
Odpowiedzialność:
- zbudować z wielu partii mapę powtarzalnych problemów użytkownika.

## Co agregować
- liczba błędów na kategorię,
- błędy według fazy partii,
- błędy według otwarcia,
- błędy według koloru,
- błędy według typu pozycji,
- najczęstsze motywy taktyczne,
- najczęstsze problemy strategiczne,
- średni centipawn loss,
- trend poprawy w czasie.

### Wnioski, które system ma umieć generować
- najczęściej przeoczasz taktykę po przekątnej,
- za często grasz pionami przy królu,
- w debiucie opóźniasz rozwój figur lekkich,
- w końcówkach zbyt pasywnie ustawiasz króla,
- w partiach hiszpańskich częściej tracisz tempo niż w włoskich.

### Zadania
- [x] Zbudować `PlayerProfileService`.
- [x] Wprowadzić miesięczne / kwartalne agregaty.
- [x] Wyliczać top 3 najczęstsze błędy.
- [x] Dodać rekomendacje treningowe na podstawie profilu.

---

## Warstwa 8 - Moduł rekomendacji treningowych
Odpowiedzialność:
- zamienić wykryte błędy w konkretne zalecenia treningowe.

## Przykłady rekomendacji
- jeśli dominują blundery figur -> trening "undefended pieces" i skany zagrożeń,
- jeśli dominują błędy debiutowe -> przegląd zasad debiutowych i analiza własnych pierwszych 10 ruchów,
- jeśli dominują błędy w końcówkach -> zestaw ćwiczeń końcówkowych,
- jeśli dominują problemy z bezpieczeństwem króla -> motywy ataku na króla i struktury osłonowe.

### Zadania
- [x] Zmapować kategorie błędów na typy treningu.
- [x] Dodać gotowe ćwiczenia lub checklisty.
- [x] Pokazywać 1-3 priorytety zamiast długiej listy.

Stan po bieżącej iteracji:
- rekomendacje uwzględniają dominującą fazę partii dla danego wzorca błędu,
- rekomendacje uwzględniają kolor, którym najczęściej pojawia się problem,
- rekomendacje podpowiadają też najczęstsze otwarcia / kody ECO powiązane z danym motywem,
- widok profilu pokazuje już kontekst rekomendacji obok checklisty i sugerowanych ćwiczeń,
- profil generuje też tygodniowy plan treningowy oparty o 1-3 najwyższe priorytety.

---

## Model danych

## Encje podstawowe

### `ChessGame`
```csharp
public class ChessGame
{
    public Guid Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Pgn { get; set; } = string.Empty;
    public string WhitePlayer { get; set; } = string.Empty;
    public string BlackPlayer { get; set; } = string.Empty;
    public int? WhiteElo { get; set; }
    public int? BlackElo { get; set; }
    public DateTime PlayedAtUtc { get; set; }
}
```

### `MoveAnalysis`
```csharp
public class MoveAnalysis
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public int Ply { get; set; }
    public int MoveNumber { get; set; }
    public string Side { get; set; } = string.Empty;
    public string FenBefore { get; set; } = string.Empty;
    public string FenAfter { get; set; } = string.Empty;
    public string PlayedMoveUci { get; set; } = string.Empty;
    public string BestMoveUci { get; set; } = string.Empty;
    public int? EvalBeforeCp { get; set; }
    public int? EvalAfterCp { get; set; }
    public int? CentipawnLoss { get; set; }
    public bool IsCandidateMistake { get; set; }
    public string Phase { get; set; } = string.Empty;
}
```

### `MistakeTag`
```csharp
public class MistakeTag
{
    public Guid Id { get; set; }
    public Guid MoveAnalysisId { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string EvidenceJson { get; set; } = string.Empty;
}
```

### `MoveExplanation`
```csharp
public class MoveExplanation
{
    public Guid Id { get; set; }
    public Guid MoveAnalysisId { get; set; }
    public string ShortExplanation { get; set; } = string.Empty;
    public string DetailedExplanation { get; set; } = string.Empty;
    public string TrainingHint { get; set; } = string.Empty;
}
```

### `PlayerProfile`
```csharp
public class PlayerProfile
{
    public Guid Id { get; set; }
    public string PlayerKey { get; set; } = string.Empty;
    public string SummaryJson { get; set; } = string.Empty;
    public DateTime LastUpdatedUtc { get; set; }
}
```

---

## Moduły aplikacyjne

## 1. `PgnImportService`
Odpowiada za:
- wczytanie PGN,
- parsowanie partii,
- utworzenie struktury ruchów.

## 2. `PositionExtractionService`
Odpowiada za:
- generowanie FEN przed i po ruchach,
- przygotowanie danych do silnika.

## 3. `StockfishService`
Odpowiada za:
- komunikację UCI,
- analizę pozycji,
- pobieranie best move i eval.

## 4. `MoveScoringService`
Odpowiada za:
- przeliczenie jakości ruchu,
- oznaczenie ruchów podejrzanych.

## 5. `FeatureExtractionService`
Odpowiada za:
- policzenie cech pozycji i błędu.

## 6. `MistakeClassifier`
Odpowiada za:
- przypisanie kategorii błędu,
- confidence,
- evidence.

## 7. `AdviceGenerator`
Odpowiada za:
- stworzenie krótkiego i rozszerzonego opisu błędu,
- wybór implementacji generatora porady niezależnie od UI i pipeline,
- umożliwienie późniejszego podpięcia mocniejszego generatora lokalnego bez przebudowy analizy partii.

## 8. `PlayerProfileService`
Odpowiada za:
- agregację danych z wielu partii,
- profil błędów,
- rekomendacje treningowe.

## 9. `AnalysisOrchestrator`
Odpowiada za:
- uruchomienie całego pipeline.

---

## Pipeline analizy partii

```text
PGN import
-> ekstrakcja ruchów i FEN
-> analiza silnikowa ruchów gracza
-> scoring jakości ruchów
-> wybór kandydatów na błędy
-> ekstrakcja cech
-> klasyfikacja typu błędu
-> generacja komentarza
-> zapis do bazy
-> aktualizacja profilu gracza
```

---

## Roadmap wdrożenia

## Stan etapów na dziś

### Etap 1 - Solidny fundament
Status: **wdrożone MVP v1**

Zrealizowane:
- import PGN,
- replay partii i snapshoty FEN,
- analiza silnikiem pozycji przed i po ruchu,
- `best move`, PV, `centipawn loss`,
- analiza jednej partii dla wybranego koloru,
- widok wyników analizy w aplikacji.

### Etap 2 - Kandydaci i priorytetyzacja
Status: **wdrożone podstawowe v1**

Zrealizowane:
- wybór `mistake` / `blunder`,
- limit wybranych `inaccuracy`,
- scalanie sąsiednich ruchów tego samego motywu.

Do dopracowania:
- lepszy ranking edukacyjnej wartości pozycji,
- filtrowanie szumu w długich partiach,
- odróżnianie pozycji krytycznej od pozycji tylko trochę gorszej.

### Etap 3 - Klasyfikator heurystyczny
Status: **wdrożone podstawowe v1**

Zrealizowane:
- podstawowe heurystyki materiałowe, taktyczne, królewskie, debiutowe i końcówkowe,
- `label`, `confidence`, `evidence`.

Do dopracowania:
- więcej cech pozycji,
- lepsze rozróżnienie pomiędzy `missed_tactic` i `hanging_piece`,
- dokładniejsza klasyfikacja błędów strategicznych.

### Etap 4 - Komentarze edukacyjne
Status: **wdrożone podstawowe v1**

Zrealizowane:
- krótki komentarz z lepszym ruchem i wskazówką treningową.
- rozszerzony komentarz rozwijający: co było złe, dlaczego, co było lepsze i jak rozpoznawać podobny motyw.
- kontrakt `IAdviceGenerator`, dzięki któremu obecny generator szablonowy można później zastąpić mocniejszą implementacją lokalną.
- lokalny tryb `Adaptive`, który wzbogaca komentarz bez korzystania z zewnętrznego API.
- lokalny log diagnostyczny generatora porad do pliku JSONL.

Do dopracowania:
- stylistyczne dopracowanie wariantu rozszerzonego,
- dalsze strojenie poziomów trudności komentarza,
- dodanie polityki wyboru generatora porady zależnie od konfiguracji lokalnej,
- dalsze dopracowanie prezentacji PV i SAN zamiast surowego UCI tam, gdzie to ma sens.

### Etapy 5-7
Status: **rozpoczęte w wersji podstawowej dla profilu gracza**

Te etapy są nadal aktualne, ale powinny być podejmowane dopiero po uporządkowaniu UX, wydajności i persystencji pojedynczej analizy.

## Etap 1 - Solidny fundament
Cel: mieć stabilną analizę jednej partii.

### Zakres
- import PGN,
- analiza Stockfishem,
- centipawn loss,
- lista błędów,
- prosty zapis do bazy.

### Done criteria
- system analizuje pełną partię,
- dla każdego ruchu gracza zapisuje eval i best move,
- potrafi wskazać top błędy partii.

Status:
- [x] gotowe w obecnym repo dla analizy jednej importowanej partii

---

## Etap 2 - Kandydaci i priorytetyzacja
Cel: nie komentować wszystkiego, tylko rzeczy ważne.

### Zakres
- filtry ruchów,
- ranking błędów,
- punkty zwrotne partii,
- redukcja szumu.

### Done criteria
- użytkownik dostaje sensowną listę najważniejszych błędów,
- komentarze nie są zalane drobiazgami.

Status:
- [x] gotowe jako wersja podstawowa
- [x] ranking preferuje bardziej edukacyjne momenty z pozycji grywalnych lub wygranych, zamiast przeceniać podobne błędy z pozycji już przegranych
- [ ] wymaga dopracowania rankingu i jakości selekcji

---

## Etap 3 - Klasyfikator heurystyczny
Cel: przypisać typ błędu.

### Zakres
- pierwsze heurystyki,
- podstawowe cechy pozycji,
- confidence,
- evidence.

### Done criteria
- system potrafi oznaczyć przynajmniej kilka głównych kategorii błędów,
- label nie jest losowy, tylko wynika z danych.

Status:
- [x] gotowe jako heurystyczne v1

---

## Etap 4 - Komentarze edukacyjne
Cel: generować komentarz wartościowy dla gracza.

### Zakres
- szablony komentarzy,
- wariant krótki i rozszerzony,
- dopasowanie do poziomu użytkownika.

### Done criteria
- każdy ważny błąd ma czytelne wyjaśnienie,
- komentarz mówi nie tylko "to zły ruch", ale też dlaczego.

Status:
- [x] gotowe jako krótki komentarz v1
- [ ] wymaga wariantu rozszerzonego i lepszego języka opisu

---

## Etap 5 - Profil gracza
Cel: system ma rozumieć powtarzalne problemy.

### Zakres
- agregacja wielu partii,
- dashboard błędów,
- top obszary do poprawy,
- rekomendacje treningowe.

### Done criteria
- po analizie wielu partii system potrafi wskazać stałe słabości gracza.

---

## Etap 6 - Hybryda AI
Cel: poprawić jakość klasyfikacji i opisu bez trenowania od zera.

### Zakres
- wykorzystanie lokalnego generatora adaptacyjnego do doprecyzowania opisu,
- ewentualnie wykorzystanie lokalnego modelu on-device do wyboru labelu spośród kandydatów,
- walidacja wyników przez reguły.

### Done criteria
- komentarze brzmią naturalniej,
- klasyfikacja jest trafniejsza w trudnych pozycjach.

---

## Etap 7 - Dane pod przyszły fine-tuning
Cel: przygotować się na późniejszy rozwój.

### Zakres
- logowanie przykładów,
- ręczne poprawki etykiet,
- zbiór dobrych komentarzy,
- ocena jakości odpowiedzi.

### Done criteria
- masz dataset, który faktycznie nadaje się do treningu lub fine-tuningu.

---

## Metryki jakości

## Metryki techniczne
- czas analizy jednej partii,
- liczba pozycji analizowanych na minutę,
- stabilność komunikacji z silnikiem,
- skuteczność cache.

## Metryki produktu
- ile komentarzy użytkownik uznaje za trafne,
- ile etykiet błędów jest poprawnych,
- ile komentarzy jest zbyt ogólnych,
- czy użytkownik rozumie, co poprawić.

## Metryki szachowe
- zgodność wykrytych krytycznych momentów z oceną silnika,
- trafność klasyfikacji błędów,
- pokrycie najczęstszych wzorców błędów.

---

## Testy

## Testy jednostkowe
- parser PGN,
- ekstrakcja FEN,
- scoring ruchu,
- progi centipawn loss,
- heurystyki klasyfikatora.

## Testy integracyjne
- komunikacja z Stockfishem,
- pełna analiza partii,
- zapis wyników do bazy.

## Testy jakościowe
- ręczna ocena 50-100 partii,
- porównanie etykiet systemu z oceną człowieka,
- poprawa heurystyk na podstawie błędnych klasyfikacji.

---

## Ryzyka projektowe

### 1. Nadmierne uproszczenie
Ryzyko:
- system będzie mówił prawdę techniczną, ale nie będzie edukacyjnie przydatny.

Ograniczenie:
- dodać warstwę komentarzy opartą o motyw i wskazówkę treningową.

### 2. Zbyt duża zależność od eval
Ryzyko:
- system wie, że ruch był zły, ale nie wie dlaczego.

Ograniczenie:
- rozbudować cechy pozycji i heurystyki klasyfikacyjne.

### 3. Zbyt dużo komentarzy
Ryzyko:
- użytkownik dostaje szum zamiast wiedzy.

Ograniczenie:
- komentować tylko pozycje o wysokiej wartości edukacyjnej.

### 4. Słaba trafność labeli
Ryzyko:
- etykiety błędów będą wyglądać losowo.

Ograniczenie:
- wprowadzić confidence + evidence + walidację ręczną.

### 5. Zbyt wczesny fine-tuning
Ryzyko:
- dużo pracy, mały zysk.

Ograniczenie:
- najpierw dopracować pipeline i zbierać dobre dane.

---

## Kolejność implementacji - praktyczna

## Najbliższe kroki implementacji

To jest aktualna kolejka prac po wdrożeniu MVP jednej partii.

### Krok 1 - uporządkowanie UI i UX analizy
- [x] dodać przejście z listy błędów do konkretnej pozycji na planszy,
- [x] podświetlać zagrany ruch i `best move`,
- [x] pokazywać SAN obok UCI,
- [x] dodać podstawowe filtry: wszystkie / blundery / błędy / niedokładności,
- [x] poprawić czytelność szczegółów analizy w oknie wyników.

### Krok 2 - usunięcie resztek duplikacji logiki z `MainForm`
- [ ] ograniczyć `MainForm` do prezentacji i sterowania,
- [x] oprzeć replay importowanych ruchów na `ChessGame` oraz FEN snapshotach,
- [x] oprzeć walidację i wykonywanie ruchów na `ChessGame`,
- [ ] nie rozwijać już lokalnej logiki SAN / legal moves w formularzu.

Stan po bieżącej iteracji:
- dawny `Form1` został przemianowany na `MainForm`,
- renderowanie planszy i mapowanie kliknięć zostało wydzielone do `ChessBoardControl`,
- `MainForm` pełni teraz bardziej rolę koordynatora UI niż miejsca do ręcznego rysowania planszy.

### Krok 3 - wzmocnienie heurystyk i scoringu
- [x] dodać pierwszą dodatkową warstwę cech pozycji do klasyfikatora,
- [x] lepiej wykrywać stratę materiału po wymuszonej sekwencji,
- [x] dopracować rozpoznawanie błędów debiutowych i końcówkowych,
- [x] poprawić `confidence`,
- [x] dodać prosty cache analiz po FEN.

Stan po bieżącej iteracji:
- klasyfikator uwzględnia liczbę rozwiniętych figur lekkich przed i po ruchu,
- ruchy typu wczesny hetman / wieża / pion skrzydłowy są mocniej wiązane z brakiem rozwoju,
- końcówki lepiej rozpoznają przegapioną centralizację króla,
- `hanging_piece` jest lepiej odróżniane od `missed_tactic` dzięki analizie bezpieczeństwa pola i natychmiastowej opłacalności bicia,
- pojawiła się pierwsza bardziej strategiczna etykieta `piece_activity` dla ruchów obniżających aktywność figury w middlegame,
- selektor błędów dla `inaccuracy` bierze pod uwagę nie tylko CPL, ale też wagę motywu, confidence, krytyczność momentu i dywersyfikację tematów,
- testy obejmują nowe heurystyki pozycyjne i aktualny wynik end-to-end.

### Krok 4 - persystencja jednej analizy
- [x] dodać `IAnalysisStore`,
- [x] dodać implementację SQLite,
- [x] zapisywać `ImportedGame`, replay, wyniki analizy i wybrane błędy,
- [x] umożliwić ponowne otwarcie zapisanej analizy.

Stan po bieżącej iteracji:
- `GameAnalysisCache` korzysta z pamięci procesu oraz trwałego magazynu SQLite,
- zapis i odczyt wyników działa bez dodatkowych paczek NuGet, przez systemowe `winsqlite3.dll`,
- po ponownym imporcie tej samej partii analiza i stan okna mogą wrócić bez ponownego liczenia,
- stan okna obejmuje już także wybrany poziom wyjaśnień,
- aplikacja zapisuje też same zaimportowane PGN i pozwala filtrować je po graczu, dacie, ECO, wyniku lub serwisie,
- z poziomu biblioteki zapisanych partii można też usunąć PGN razem z powiązaną analizą i stanem okna,
- w UI statystyk i zapisanych partii kody ECO są tłumaczone na bardziej czytelne nazwy debiutów dla gracza,
- główne okno oraz dialogi profili i zapisanych partii mają już bardziej responsywny układ przy zmianie rozmiaru,
- pojawił się też osobny widok zapisanych analiz z filtrowaniem oraz przejściem do `Load Game` lub `Open Analysis`,
- dalsze rozwinięcie historii, np. bogatsze grupowanie, sortowanie i bardziej szczegółowe statystyki zapisanych analiz, nadal pozostaje kolejnym krokiem.

### Krok 5 - profil gracza i analiza wielu partii
- [x] agregować wyniki wielu zapisanych analiz,
- [x] wyliczać top kategorie błędów,
- [x] dodać trendy i podstawowe rekomendacje treningowe.

Stan po bieżącej iteracji:
- `PlayerProfileService` agreguje zapisane analizy po analizowanym graczu,
- profil pokazuje liczbę przeanalizowanych partii, średni CPL, top etykiety błędów, fazy gry i otwarcia,
- pojawił się prosty trend miesięczny i kwartalny oraz 1-3 priorytety treningowe z checklistą i sugerowanymi ćwiczeniami,
- profil układa też deterministyczny tygodniowy plan treningowy z dniami, aktywnościami i kryterium ukończenia,
- w UI głównego okna można otworzyć widok profili i filtrować graczy po nazwie,
- kolejnym krokiem pozostaje bardziej zaawansowane grupowanie historyczne, lepsze trendy, bardziej przyjazny układ `Summary` + `Deep dive`, a później np. automatyczne śledzenie realizacji takiego planu.

### Krok 6 - AdviceGenerator i ścieżka lokalna
- [x] wydzielić `IAdviceGenerator` oraz szablonowe `TemplateAdviceGenerator`,
- [x] dodać konfigurację wyboru generatora porady,
- [x] przygotować osobną implementację `LocalHeuristicAdviceGenerator`,
- [x] logować wejście/wyjście generatora porady do oceny jakości komentarzy.

### Krok 7 - llama-server jako trwały serwer LLM
- [x] `LlamaCppServerConfig` — konfiguracja serwera (port, timeouty, ścieżki),
- [x] `LlamaCppServerResolver` — automatyczne wykrywanie `llama-server.exe` w standardowych lokalizacjach,
- [x] `LlamaCppServerManager` — singleton zarządzający procesem serwera (lazy start, health check, shutdown przy zamknięciu aplikacji),
- [x] `LlamaCppHttpAdviceModel` — implementacja `ILocalAdviceModel` przez HTTP POST `/completion`,
- [x] priorytet w `AdviceRuntimeCatalog`: serwer > cli > custom > heurystyki,
- [x] `AdviceGeneratorFactory.CreateBulkAnalysisGenerator()` korzysta z LLM gdy serwer jest dostępny,
- [x] shutdown hook w `Program.cs` zapewniający zamknięcie procesu serwera razem z aplikacją,
- [x] poprawiony parser (`LocalModelAdviceResponseParser`) radzący sobie z echowanymi promptami w stdout `llama-chat`.

Stan po bieżącej iteracji:
- serwer nasłuchuje wyłącznie na `127.0.0.1` — zero ekspozycji sieciowej,
- port wybierany automatycznie (wolny TCP) lub konfigurowany zmienną `STOCKIFHSGUI_LLAMA_SERVER_PORT`,
- model ładowany raz przy pierwszym zapytaniu, potem obsługuje wiele requestów bez restartu,
- czas odpowiedzi per-request spada z ~10-30s (ładowanie modelu + inference w `llama-cli`) do ~0.5-2s (sam inference przez HTTP),
- analiza zbiorcza wielu ruchów w partii korzysta z LLM automatycznie gdy serwer jest dostępny,
- gdy `llama-server.exe` nie istnieje, system automatycznie fallbackuje na `llama-cli` lub heurystyki.

## Sprint 1
- [x] Import PGN
- [x] Generowanie FEN
- [x] Integracja ze Stockfishem
- [x] Eval before / after
- [x] Best move
- [x] Zapis `MoveAnalysis`

## Sprint 2
- [x] Centipawn loss
- [x] Detekcja kandydatów na błąd
- [x] Ranking top błędów partii
- [x] Pierwszy widok wyników

## Sprint 3
- [x] Feature extraction
- [x] Pierwsze kategorie błędów
- [x] `MistakeClassifier`
- [x] Evidence i confidence

## Sprint 4
- [x] Generator komentarzy
- [ ] Wersja krótka i rozszerzona
- [x] Wskazówki treningowe

## Sprint 5
- [x] Profil gracza
- [x] Agregacja wielu partii
- [x] Top błędy i trendy

## Sprint 6
- [x] Lokalny wybór generatora porady
- [x] Lokalny log diagnostyczny generatora
- [x] Poprawa jakości opisów (prompt tuning)
- [x] Lepsza personalizacja (historia błędów gracza w prompcie)

## Sprint 7
- [x] `llama-server` jako trwały serwer LLM z lifecycle powiązanym z aplikacją
- [x] HTTP POST `/completion` zamiast procesu per-request
- [x] Priorytet runtime: server > cli > heurystyki
- [x] Analiza zbiorcza z LLM gdy serwer dostępny
- [x] Poprawka parsera dla echowanych promptów

## Sprint 8
- [x] Prompt tuning `AdvicePromptFormatter` pod Qwen 2.5 3B
- [x] Osobny ton na każdy `ExplanationLevel` (Beginner / Intermediate / Advanced)
- [x] FEN pozycji w prompcie — model widzi planszę
- [x] Per-field word limits zamiast ogólnego "max 80 words"
- [x] One-shot example dopasowany do `ExplanationLevel` + label błędu
- [x] Ustrukturyzowane sekcje: Position, Played move, Engine verdict, Game context

## Sprint 9
- [x] `PlayerMistakeProfile` — lekki model profilu gracza na potrzeby promptu
- [x] `PlayerMistakeProfileProvider` — buduje profil z historii analiz w store (min. 2 gry)
- [x] `AdvicePromptContext.PlayerProfile` — nowe pole opcjonalne
- [x] Sekcja "Player history" w prompcie z top 3 recurring patterns, avg CPL, weakest phase
- [x] Instrukcja dla LLM: jeśli bieżący błąd pasuje do wzorca, wspomnieć o tym
- [x] Profil ładowany raz per partia (nie per ruch) — zero overhead
- [x] Graceful fallback gdy store niedostępny (catch w TryBuild)

## Kolejne sprinty od obecnego stanu

### Sprint 10 - Klasyfikacja v1.5
- [x] Dopracować rozdzielenie `missed_tactic` vs `material_loss`
- [x] Dopracować rozdzielenie `missed_tactic` vs `hanging_piece`
- [ ] Ograniczyć liczbę wyników `unclassified`
- [x] Rozszerzyć heurystyki `king_safety` i `endgame_technique`
- [x] Dodać testy regresyjne dla granicznych przypadków klasyfikatora

Stan po bieżącej iteracji:
- `king_safety` uwzględnia już nie tylko osłabienie po roszadzie krótkiej, ale też analogiczne osłabienie po roszadzie długiej oraz wyjście królem z bezpiecznego schronienia.
- `endgame_technique` łapie teraz także pasywny odwrót króla i spadek aktywności króla w końcówce, nie tylko brak centralizacji.
- klasyfikator potrafi preferować `missed_tactic`, gdy przegapiona szansa taktyczna wyraźnie przewyższa małą stratę materiału.
- `piece_activity` obejmuje już także część pozycyjnych ruchów, w których najlepsza kontynuacja polegała głównie na aktywizacji figury, a nie na czystej taktyce.
- nadal warto ograniczyć pulę starych lub mało informacyjnych przypadków `unclassified` w danych historycznych oraz dalej zmniejszać zbyt szeroki fallback do `missed_tactic`.

Cel sprintu:
żeby etykiety błędów były bardziej trafne i stabilne, zanim dołożymy kolejne warstwy opisu i profilowania.

### Sprint 11 - Selekcja błędów v1.5
- [x] Ulepszyć ranking `MistakeSelector`, żeby lepiej wybierał momenty edukacyjnie najważniejsze
- [x] Mocniej preferować pierwszy punkt zwrotny zamiast kilku podobnych błędów pod rząd
- [x] Lepiej scalać sąsiadujące błędy tego samego motywu
- [x] Ograniczyć szum z pozycji już przegranych
- [x] Dodać testy selekcji na partiach z wieloma podobnymi błędami

Stan po bieżącej iteracji:
- selektor scala już nie tylko identyczne etykiety obok siebie, ale też bliskie w czasie błędy z tej samej rodziny motywów, jeśli pozycja realnie nie zdążyła się odbudować;
- w obrębie scalonej narracji reprezentant częściej pokazuje pierwszy punkt zwrotny, a nie tylko najpóźniejszy ruch z największym CPL;
- ranking dalej premiuje momenty edukacyjne z pozycji grywalnych lub wygranych i ogranicza szum z pozycji już wyraźnie przegranych;
- testy obejmują teraz także przypadki: merge przez jeden spokojniejszy ruch, merge pokrewnych motywów materiałowych oraz wybór pierwszego punktu zwrotnego jako lead move.

Cel sprintu:
żeby użytkownik widział krótszą, bardziej sensowną listę błędów, a nie kilka wariantów tego samego problemu.

### Sprint 12 - Advice v2
- [x] Dokończyć pełną wersję krótką i rozszerzoną dla komentarzy
- [x] Ujednolicić strukturę porady: `what`, `why`, `better move`, `watch next time`
- [x] Poprawić naturalność języka dla generatora heurystycznego i LLM fallback
- [x] Dopiąć limity długości tak, by komentarze były krótkie, ale konkretne
- [x] Dodać testy jakości formatu odpowiedzi i fallbacków

Stan po bieżącej iteracji:
- `TemplateAdviceGenerator` buduje teraz bardziej przewidywalny `detailed_text` w układzie `What / Why / Better / Watch next time`, zamiast luźnego akapitu;
- `short_text` jest krótsze i bardziej czytelne, a generator zachowuje odrębny ton dla poziomów `Beginner / Intermediate / Advanced`;
- `LocalHeuristicAdviceGenerator` dokleja lokalne sygnały pozycyjne i kontekst otwarcia do rozszerzonego opisu, zamiast przeładowywać `short_text`;
- prompt dla local model wyraźnie wymaga tej samej struktury w `detailed_text`, więc heurystyki i LLM idą w tym samym kierunku UX;
- testy pilnują teraz zarówno struktury sekcji, jak i jakości fallbacków oraz limitów długości.

Cel sprintu:
żeby porady były czytelne, spójne i naprawdę pomocne, niezależnie od tego, czy odpowiada model lokalny czy heurystyka.

### Sprint 13 - Profil gracza v2
- [x] Rozdzielić błędy częste od błędów najdroższych
- [x] Wzmocnić priorytety treningowe na podstawie realnego kosztu błędów
- [x] Dodać porównanie okresów: ostatnie gry vs starsza historia
- [x] Pokazywać bardziej konkretne przykłady motywów prowadzących do rekomendacji
- [x] Uzupełnić profil o prosty sygnał postępu lub regresu

Stan po bieżącej iteracji:
- raport profilu rozdziela teraz `TopMistakeLabels` od `CostliestMistakeLabels`, więc widać osobno to, co wraca często, i to, co kosztuje najwięcej CPL;
- priorytety treningowe są budowane na bazie połączonego sygnału: częstość, koszt błędów i liczba wyróżnionych momentów, zamiast samej liczby wystąpień;
- profil pokazuje prosty `ProgressSignal`, który porównuje ostatnią próbkę gier do wcześniejszej i oznacza kierunek jako `Improving`, `Stable`, `Regressing` albo `InsufficientData`;
- widok profili pokazuje już sekcję błędów najdroższych oraz sekcję trendu postępu/regresu;
- nadal warto dołożyć bardziej konkretne przykłady pozycji lub motywów prowadzących do rekomendacji treningowych.

Cel sprintu:
żeby profil nie był tylko statystyką, ale faktycznie wskazywał, nad czym gracz powinien pracować najpierw.

### Sprint 14 - Dane pod local AI
- [x] Logować przypadki niskiej pewności klasyfikatora i słabe jakościowo porady
- [x] Zbierać bogatszy payload diagnostyczny dla przyszłego strojenia promptów lub modeli
- [x] Dodać prosty raport jakości: ile wyników wpada do `unclassified`, ile porad kończy się fallbackiem
- [x] Przygotować dataset eksportowy pod przyszłe eksperymenty z lokalnym modelem
- [x] Utrzymać pełną pracę offline bez zależności od zewnętrznych providerów

Stan po bieżącej iteracji:
- `DiagnosticMistakeClassifier` owija `MistakeClassifier` i automatycznie zapisuje do JSONL każdy przypadek z niską pewnością (< 0.70) lub generycznym fallbackiem `missed_tactic`; logowanie jest fire-and-forget i nigdy nie blokuje analizy;
- `AnalysisQualityReporter` czyta oba pliki JSONL (klasyfikator + advice traces) i generuje raport markdown z tabelą etykiet, wskaźnikiem fallbacków i podziałem przyczyn; uruchamialny przez CLI `--quality-report`;
- `DatasetExporter` eksportuje wszystkie `StoredMoveAnalysis` do JSONL i CSV z kompletem pól potrzebnych do eksperymentów z lokalnym modelem; uruchamialny przez `--export-dataset`;
- `ProfileMistakeExample` dostarcza do profilu gracza konkretne pozycje (FEN, CPL, SAN, best move, opening) dla każdego dominującego motywu błędu;
- cały system działa w 100% offline; brak nowych zależności sieciowych.

### Sprint 15 - UI / workflow polish
- [x] Przebudować profil gracza na dwie warstwy prezentacji: krótki blok `Summary` na górze i sekcję `Deep dive` niżej
- [x] W `Summary` pokazywać: największy problem, drugi problem, najsłabszą fazę partii, najbardziej problematyczne otwarcie oraz krótki sygnał ostatniego trendu
- [x] Pokazać w profilach i analizach bardziej klikalne przykłady pozycji
- [x] Ułatwić przejście z profilu gracza do konkretnej partii i konkretnego błędu
- [x] Dodać lepsze badge, filtry i oznaczenia najważniejszych błędów
- [x] Zamienić techniczne nazwy sekcji na bardziej naturalne, np. `Key mistakes`, `Most expensive mistakes`, `What to work on`, `Recent trend`
- [x] Uprościć nazwy priorytetów treningowych tak, aby brzmiały naturalnie dla gracza, np. `Avoid Losing Material`, `Scan for Tactics`, `Finish Development First`
- [x] Dodać krótki blok `What to fix first` z 2-3 bezpośrednimi, operacyjnymi wskazówkami
- [x] Przebudować widok profilu na sekcje zwijane / rozwijane, tak aby bloki typu `overview`, `mistake labels`, `openings`, `phases`, `training priorities` i `history/trend` nie tworzyły jednej długiej ściany tekstu
- [x] W sekcji historii i trendów dodać listę rozwijaną dla poszczególnych dni, tak aby po rozwinięciu dnia było widać partie, highlighty, średni CPL i najważniejsze motywy błędów tylko dla tej daty
- [x] Dodać walidację spójności przed renderem, tak aby liczby w profilu, opisy trendu i sumy sekcji nie mogły sobie widocznie przeczyć
- [x] Poprawić prezentację otwarć, używając bardziej ludzkich nazw i sensownych fallbacków, np. `B00 - King's Pawn setups after 1.e4` zamiast surowych lub mało pomocnych nazw `Uncommon ...`
- [x] Zrobić z tygodniowego planu sekcję opcjonalną, pokazywaną dopiero po rozwinięciu albo po akcji typu `Show training plan`
- [x] Dalej odchudzać formularze i separować logikę UI od pipeline analizy
- [x] Dopracować responsywność i skalowanie okien pomocniczych

Cel sprintu:
żeby coraz mocniejsza logika analityczna była też wygodna w codziennym użyciu, a profil dawał się zrozumieć w 10-15 sekund zamiast wyglądać jak raport debugowy.

### Rekomendowana kolejność realizacji
1. Sprint 10 - Klasyfikacja v1.5
2. Sprint 11 - Selekcja błędów v1.5
3. Sprint 12 - Advice v2
4. Sprint 13 - Profil gracza v2
5. Sprint 14 - Dane pod local AI
6. Sprint 15 - UI / workflow polish

### Uwaga wykonawcza
Najpierw warto dopracować trafność klasyfikacji i selekcji, bo to jest jakość wejścia dla profilu gracza i przyszłych porad generowanych przez lokalny model. Dopiero na tym fundamencie sensownie rozwijać warstwę local AI i dalszą personalizację.

## Ścieżka Do Opening Trainera I Treningu Personalnego

Ta sekcja rozpisuje kolejne sprinty, które mają doprowadzić do trzech celów:
- `opening trainer`,
- plan treningowy oparty na realnych słabościach użytkownika,
- użycie lokalnego LLM do formatowania profilu i planu pod poziom gracza, bez oddawania mu logiki priorytetów.

Założenie architektoniczne:
- logika priorytetów, scoringu i selekcji pozostaje deterministyczna,
- LLM służy do tłumaczenia, personalizacji tonu i lepszego formatowania,
- system zachowuje pełny fallback offline.

### Sprint 16 - Przykłady Do Profilu
- [x] Dodać do profilu `example positions` dla dominujących motywów błędów.
- [x] Dla każdej rekomendacji pokazywać 2-3 konkretne przykłady z własnych partii użytkownika.
- [x] Przy każdym przykładzie pokazać: label, CPL, fazę partii, opening i lepszy ruch.
- [x] Dodać ranking przykładów: najczęstszy, najdroższy, najbardziej reprezentatywny.
- [x] Umożliwić przejście `profil -> przykład -> plansza -> analiza`.
- [x] Spiąć przykłady z przebudowanym widokiem profilu, aby każda rozwijana sekcja i każdy rozwijany dzień mogły pokazywać własne przykłady oraz szybkie przejścia do partii.
- [x] Sprawić, aby sekcje `Summary` i `What to fix first` korzystały z tej samej puli przykładów, tak aby każde ogólne stwierdzenie dało się prześledzić do konkretnych pozycji.

Stan po bieżącej iteracji:
- `ProfileMistakeExample` dostarcza już do profilu konkretne pozycje dla dominujących motywów błędów wraz z FEN, CPL, SAN, `best move` i openingiem,
- sprint pozostaje częściowo otwarty po stronie prezentacji UI, rankingu przykładów i pełnego spięcia z nawigacją `profil -> przykład -> plansza -> analiza`.

Cel sprintu:
żeby profil przestał być tylko zestawem agregatów i zaczął pokazywać rzeczywiste momenty z partii, które stoją za rekomendacjami.

### Sprint 17 - Silnik Planu Treningowego v1
- [x] Wydzielić `TrainingPlanService`.
- [x] Zbudować `TrainingPlanReport` na podstawie `PlayerProfileReport`.
- [x] Wyliczać priorytety z połączenia: częstość, koszt CPL, trend i faza partii.
- [x] Rozdzielić tematy na `core weakness`, `secondary weakness`, `maintenance topic`.
- [x] Dodać prosty budżet czasu treningowego na tydzień.

Stan po bieżącej iteracji:
- profil układa już deterministyczny tygodniowy plan treningowy z dniami, aktywnościami i kryterium ukończenia,
- istnieje więc funkcjonalny zalążek tego sprintu, ale nadal do domknięcia pozostaje wydzielenie osobnej warstwy `TrainingPlanService` / `TrainingPlanReport` oraz dopracowanie modelu priorytetów i budżetu czasu.

Cel sprintu:
żeby użytkownik dostawał realny tygodniowy plan pracy, a nie tylko opis swoich słabości.

### Sprint 18 - Trening Słabości v2
- [x] Dodać typy bloków treningowych: `tactics`, `opening review`, `endgame drill`, `game review`, `slow play focus`.
- [x] Mapować etykiety błędów na konkretne typy ćwiczeń.
- [x] Rozdzielić ćwiczenia na naprawcze, utrwalające i checklisty do gry.
- [x] Dodać prostą adaptację priorytetów na podstawie trendu poprawy lub regresu.
- [x] Dodać `why this topic now` dla każdej pozycji w planie.

Stan po bieżącej iteracji:
- rekomendacje treningowe mapują już kategorie błędów na typy treningu oraz pokazują checklistę i sugerowane ćwiczenia,
- sprint pozostaje częściowo otwarty, bo w planie nadal brakuje jawnych typów bloków treningowych, rozdziału na ćwiczenia naprawcze i utrwalające oraz pola `why this topic now`.

Cel sprintu:
żeby plan treningowy był praktyczny i tłumaczył, czemu dany temat jest teraz ważny.

### Sprint 19 - Fundament Pod Opening Trainer
- [ ] Dodać identyfikację momentu wyjścia z teorii w zaimportowanej partii.
- [ ] Wykrywać pierwszy błąd w debiucie i przypisywać go do konkretnej gałęzi openingu.
- [ ] Zbudować `OpeningWeaknessService`.
- [ ] Agregować najczęstsze problematyczne openingi i typowe sekwencje błędów.
- [ ] Dla każdego openingu zapisywać przykładowe partie, motywy błędów i referencyjne ruchy.

Cel sprintu:
zrozumieć nie tylko, że gracz ma problem w debiucie, ale dokładnie w jakim debiucie i po jakiej sekwencji ruchów.

### Sprint 20 - Opening Trainer v1
- [ ] Dodać `OpeningTrainerService`.
- [ ] Wprowadzić tryb `line recall`, gdzie użytkownik odtwarza właściwy ruch z pozycji.
- [ ] Wprowadzić tryb `mistake repair`, gdzie użytkownik poprawia własny błąd z partii.
- [ ] Wprowadzić tryb `branch awareness`, gdzie użytkownik trenuje typowe odpowiedzi przeciwnika.
- [ ] Dodać podstawowy scoring: poprawny ruch, ruch grywalny, ruch błędny.

Cel sprintu:
uruchomić pierwszą wersję trenera debiutowego opartego na własnych partiach użytkownika i lokalnych danych.

### Sprint 21 - Opening Trainer v2 + Integracja Z Planem
- [ ] Zintegrować opening trainer z profilem gracza i planem treningowym.
- [ ] Automatycznie dodawać sesje debiutowe do planu, jeśli opening jest niestabilny lub kosztowny.
- [ ] Dodać kategorie `opening to fix now`, `opening to review later`, `opening stable`.
- [ ] Pokazać w UI listę najbardziej niestabilnych i najdroższych openingów.
- [ ] Umożliwić start treningu bezpośrednio z profilu i planu tygodniowego.

Cel sprintu:
żeby trener debiutowy stał się naturalną częścią całego workflow treningowego, a nie osobnym modułem.

### Sprint 22 - Real Opening Book I Teoria Debiutowa
- [ ] Dodać import realnej bazy debiutowej / książki otwarć w formacie PGN lub Polyglot `.bin`.
- [ ] Zbudować lokalny indeks teorii debiutowej po FEN / pozycji kanonicznej: `position -> candidate theory moves`.
- [ ] Rozpoznawać nazwę debiutu, wariant i subwariant na podstawie realnej sekwencji ruchów, a nie tylko kodu ECO z importowanej partii.
- [ ] Oznaczać moment wyjścia z teorii przez porównanie ruchu użytkownika z zaimportowaną książką otwarć.
- [ ] Rozróżniać lokalną kontynuację z partii użytkownika od realnej kontynuacji teoretycznej.
- [ ] Rozszerzyć scoring opening trainera tak, aby `correct`, `playable` i `wrong` mogły brać pod uwagę zarówno lokalne dane użytkownika, jak i ruchy z książki otwarć.
- [ ] Dodać metadane źródła ruchu: `user_game`, `engine_best_move`, `opening_book`, `eco_reference`.
- [ ] Zapewnić pełny fallback offline/local-only, gdy użytkownik nie zaimportował książki debiutowej.

Cel sprintu:
żeby opening trainer przestał opierać się wyłącznie na własnych partiach użytkownika i lokalnych analizach, a potrafił porównywać je z realnymi debiutami, wariantami i kontynuacjami teoretycznymi.

### Sprint 23 - LLM Formatter Profilu Gracza
- [ ] Przygotować strukturalny input do LLM z gotowego `PlayerProfileReport`.
- [ ] Dodać wyjścia: `profile_summary`, `strengths_and_weaknesses`, `what_to_focus_next`, `tone_adapted_version`.
- [ ] Dodać poziomy odbiorcy: `Beginner`, `Intermediate`, `Advanced`.
- [ ] Utrzymać zgodność wyjścia z układem UI: krótkie `Summary` najpierw, opcjonalne `Deep dive` niżej i zero debugowego stylu w tekście dla gracza.
- [ ] Utrzymać walidację, żeby model nie dodawał nowych faktów spoza danych.
- [ ] Zachować pełny heurystyczny fallback bez modelu.

Cel sprintu:
używać lokalnego LLM do formatowania i upraszczania profilu, ale nie do ustalania logiki diagnozy.

### Sprint 24 - LLM Formatter Planu Treningowego
- [ ] Przygotować strukturalny input do LLM z gotowego `TrainingPlanReport`.
- [ ] Generować krótką i rozszerzoną wersję planu tygodniowego.
- [ ] Dodawać zrozumiałe uzasadnienie priorytetów dla użytkownika.
- [ ] Dostosować ton do poziomu gracza i dostępnego czasu na trening.
- [ ] Zachować pełny fallback lokalny bez modelu.

Cel sprintu:
sprawić, żeby plan treningowy był bardziej ludzki, czytelny i motywujący, bez utraty deterministycznego rdzenia.

### Sprint 25 - Zamknięcie Pętli Treningowej
- [ ] Zapisywać wykonane sesje treningowe i wyniki z opening trainera.
- [ ] Łączyć wyniki treningu z profilem gracza i planem.
- [ ] Aktualizować priorytety tematów na podstawie realnych wyników, a nie tylko nowych analiz partii.
- [ ] Dodać statusy tematów: `new weakness`, `improving`, `stable`, `urgent`.
- [ ] Zbudować prosty dashboard `why this is your current plan`.

Cel sprintu:
zamknąć pętlę między analizą partii, profilem, planem treningowym i treningiem debiutowym.

### Rekomendowana Kolejność Po Sprint 15
1. Sprint 16 - Przykłady Do Profilu
2. Sprint 17 - Silnik Planu Treningowego v1
3. Sprint 18 - Trening Słabości v2
4. Sprint 19 - Fundament Pod Opening Trainer
5. Sprint 20 - Opening Trainer v1
6. Sprint 21 - Opening Trainer v2 + Integracja Z Planem
7. Sprint 22 - Real Opening Book I Teoria Debiutowa
8. Sprint 23 - LLM Formatter Profilu Gracza
9. Sprint 24 - LLM Formatter Planu Treningowego
10. Sprint 25 - Zamknięcie Pętli Treningowej

### Uwaga Końcowa
Najpierw warto domknąć dane, przykłady i deterministyczny plan treningowy, a dopiero potem dołożyć warstwę LLM do formatowania. Dzięki temu model będzie poprawiał UX i personalizację języka, ale nie stanie się pojedynczym punktem awarii ani źródłem halucynacji w logice treningowej.

---

## Dane do zbierania od początku

Zapisuj wszystko, co może się przydać później:
- PGN,
- FEN przed i po ruchu,
- eval before / after,
- best move,
- played move,
- PV,
- label błędu,
- confidence,
- evidence,
- wygenerowany komentarz,
- ewentualną ręczną poprawkę użytkownika lub eksperta.

To będzie fundament pod przyszłe:
- lepsze heurystyki,
- modele klasyfikacyjne,
- fine-tuning,
- ranking jakości komentarzy.

---

## MVP wersji "dobra - trudna"

Produkt uznajemy za gotowy do sensownego użycia, gdy:
- analizuje pełną partię PGN,
- wykrywa najważniejsze błędy,
- przypisuje je do sensownych kategorii,
- generuje czytelny komentarz,
- pokazuje lepszą alternatywę,
- agreguje błędy z wielu partii,
- wskazuje 3 główne obszary treningowe gracza.

### Aktualizacja definicji MVP

W praktyce projekt ma dziś dwa poziomy gotowości:

#### MVP v1 - już wdrożone
- analizuje pełną partię PGN,
- analizuje wybrany kolor,
- wykrywa najważniejsze błędy,
- przypisuje im podstawowe kategorie,
- generuje krótki komentarz i wskazówkę treningową,
- pokazuje lepszy ruch.

#### MVP v2 - kolejny sensowny cel
- zapisuje wyniki analizy,
- pozwala wracać do wcześniejszych analiz,
- agreguje wiele partii,
- wskazuje 3 główne obszary treningowe gracza.

---

## Co później

Po wdrożeniu tej wersji można rozwijać:
- personalizację pod rating gracza,
- analizę konkretnych debiutów,
- analizę partii błyskawicznych vs klasycznych,
- wykrywanie błędów pod presją czasu,
- generowanie planów treningowych tygodniowych,
- fine-tuning na własnych danych,
- porównywanie profilu użytkownika w czasie.

---

## Krótki wniosek

Najlepsza droga wdrożenia to:
- **Stockfish jako rdzeń oceny**,
- **heurystyki i cechy pozycji jako rdzeń klasyfikacji**,
- **lokalny generator porad jako warstwa tłumaczenia i personalizacji**,
- **profil gracza jako warstwa długoterminowej wartości**.

Nie buduj od razu "magicznego AI od wszystkiego". Zbuduj mocny lokalny pipeline, zbieraj dane, poprawiaj heurystyki i dopiero potem dokładaj bardziej zaawansowaną inteligencję bez uzależniania produktu od zewnętrznego dostawcy.
