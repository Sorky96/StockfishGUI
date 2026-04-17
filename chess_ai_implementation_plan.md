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
- model językowy odpowiada za tłumaczenie błędów ludzkim językiem.

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
- [x] Replay importowanej partii oparty o `ChessGame` i snapshoty FEN zamiast lokalnego parsera SAN w `Form1`.
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
- [x] Rozszerzony komentarz edukacyjny w oknie analizy.
- [x] Integracja z UI: po imporcie PGN można uruchomić analizę importowanej partii.
- [x] Integracja z UI: wybór błędu przenosi planszę do właściwej pozycji i pokazuje zagrany ruch oraz `best move`.
- [x] Integracja z UI: filtry jakości ruchu, poprawione numerowanie ruchów oraz współrzędne planszy.
- [x] Cache całego wyniku analizy partii w sesji aplikacji.
- [x] Trwały cache analizy jednej partii w SQLite między uruchomieniami aplikacji.
- [x] Przywracanie ostatniego stanu okna analizy po ponownym otwarciu dialogu.
- [x] Zapamiętywanie wybranego poziomu wyjaśnień w stanie okna analizy.
- [x] Biblioteka zapisanych partii z filtrowaniem i ponownym wczytaniem do głównej planszy.
- [x] Podstawowy `PlayerProfileService` agregujący zapisane analizy wielu partii.
- [x] Prosty widok profilu gracza z top kategoriami błędów, fazami, otwarciami i trendem miesięcznym.
- [x] Testy jednostkowe, integracyjne i end-to-end dla MVP jednej partii.

### Co jest świadomie jeszcze poza MVP
- [ ] zapis wyników analizy do SQLite,
- [ ] historia analiz i odczyt wcześniejszych partii,
- [ ] bardziej zaawansowany profil gracza z wielu partii,
- [ ] bogatsze rekomendacje treningowe oparte o dane historyczne,
- [ ] warstwa LLM,
- [ ] dalsze porządkowanie `Form1`, ale bez lokalnej logiki legal moves / SAN jako źródła prawdy.

### Zasada na dalsze etapy
Nowe funkcje powinny być dokładane do usług domenowych i modeli analitycznych, a nie do `Form1`. UI ma wywoływać pipeline i prezentować wynik.

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
- [ ] rozszerzyć heurystyki o bogatsze cechy pozycji i lepszą separację etykiet

### Etap 2 - klasyfikacja hybrydowa
Łączysz:
- heurystyki,
- cechy pozycji,
- ewentualnie model ML lub LLM do doprecyzowania etykiety.

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
- [ ] Dodać maksymalną długość komentarza.
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
- [ ] Zbudować `PlayerProfileService`.
- [ ] Wprowadzić miesięczne / kwartalne agregaty.
- [ ] Wyliczać top 3 najczęstsze błędy.
- [ ] Dodać rekomendacje treningowe na podstawie profilu.

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
- [ ] Zmapować kategorie błędów na typy treningu.
- [ ] Dodać gotowe ćwiczenia lub checklisty.
- [ ] Pokazywać 1-3 priorytety zamiast długiej listy.

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

## 7. `ExplanationGenerator`
Odpowiada za:
- stworzenie krótkiego i rozszerzonego opisu błędu.

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

Do dopracowania:
- stylistyczne dopracowanie wariantu rozszerzonego,
- dalsze strojenie poziomów trudności komentarza,
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
- wykorzystanie LLM do doprecyzowania opisu,
- ewentualnie wykorzystanie LLM do wyboru labelu spośród kandydatów,
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

### Krok 2 - usunięcie resztek duplikacji logiki z `Form1`
- [ ] ograniczyć `Form1` do prezentacji i sterowania,
- [x] oprzeć replay importowanych ruchów na `ChessGame` oraz FEN snapshotach,
- [x] oprzeć walidację i wykonywanie ruchów na `ChessGame`,
- [ ] nie rozwijać już lokalnej logiki SAN / legal moves w formularzu.

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
- selektor błędów dla `inaccuracy` bierze pod uwagę nie tylko CPL, ale też wagę motywu, confidence i krytyczność momentu,
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
- historia wielu zapisanych analiz i osobny widok przeglądania wyników nadal pozostają kolejnym krokiem.

### Krok 5 - profil gracza i analiza wielu partii
- [x] agregować wyniki wielu zapisanych analiz,
- [x] wyliczać top kategorie błędów,
- [x] dodać trendy i podstawowe rekomendacje treningowe.

Stan po bieżącej iteracji:
- `PlayerProfileService` agreguje zapisane analizy po analizowanym graczu,
- profil pokazuje liczbę przeanalizowanych partii, średni CPL, top etykiety błędów, fazy gry i otwarcia,
- pojawił się prosty trend miesięczny oraz 1-3 podstawowe priorytety treningowe,
- w UI głównego okna można otworzyć widok profili i filtrować graczy po nazwie,
- kolejnym krokiem pozostaje bardziej zaawansowane grupowanie historyczne, lepsze trendy i bogatsze rekomendacje.

## Sprint 1
- [x] Import PGN
- [x] Generowanie FEN
- [x] Integracja ze Stockfishem
- [x] Eval before / after
- [x] Best move
- [ ] Zapis `MoveAnalysis`

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
- [ ] Integracja z LLM
- [ ] Poprawa jakości opisów
- [ ] Lepsza personalizacja

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
- **LLM jako warstwa tłumaczenia i personalizacji**,
- **profil gracza jako warstwa długoterminowej wartości**.

Nie buduj od razu "magicznego AI od wszystkiego". Zbuduj mocny pipeline, zbieraj dane, poprawiaj heurystyki i dopiero potem dokładaj bardziej zaawansowaną inteligencję.
