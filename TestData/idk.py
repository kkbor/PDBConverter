import sys

def parse_dunbrack_extended(file_path):
    """
    Generator wczytujący bibliotekę Dunbrack 2010 linia po linii.
    Automatycznie pomija nagłówki i parsuje kolumny do odpowiednich typów danych.
    """
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            for line_num, line in enumerate(f, 1):
                clean_line = line.strip()
                
                # Pomiń puste linie i linie komentarza/nagłówka
                if not clean_line or clean_line.startswith('#'):
                    continue
                
                parts = clean_line.split()
                
                # Bezpiecznik: Upewnij się, że linia ma min. wymaganych kolumn
                if len(parts) < 17:
                    print(f"Ostrzeżenie: Linia {line_num} ma nieprawidłową liczbę kolumn ({len(parts)})", file=sys.stderr)
                    continue
                
                # Mapowanie danych zgodnie z nagłówkiem BBDep2010
                data = {
                    'res':       parts[0],           # Nazwa aminokwasu (np. ARG, ASN)
                    'phi':       int(parts[1]),      # Kąt PHI
                    'psi':       int(parts[2]),      # Kąt PSI
                    'counts':    int(parts[3]),      # Liczba zliczeń statystycznych
                    'rotamers':  [int(x) for x in parts[4:8]], # Konformacje R1, R2, R3, R4
                    'prob':      float(parts[8]),    # Wyglądzone prawdopodobieństwo
                    'chi_vals':  [float(x) for x in parts[9:13]], # Średnie kąty CHI1 - CHI4
                    'sig_vals':  [float(x) for x in parts[13:17]] # Odchylenia standardowe SIG1 - SIG4
                }
                
                yield data
                
    except FileNotFoundError:
        print(f"Błąd: Nie znaleziono pliku pod ścieżką {file_path}", file=sys.stderr)

# --- PRZYKŁAD UŻYCIA ---
# Gdy uda Ci się pobrać prawdziwy plik tekstowy, podmień nazwę niżej:
sciezka_do_pliku = "BBDep2010.txt" 

print("Rozpoczęcie testowego parsowania...")
for row in parse_dunbrack_extended(sciezka_do_pliku):
    # Przykład: Szukamy tylko Argininy dla konkretnego punktu na mapie Ramachandrana
    if row['res'] == 'ARG' and row['phi'] == -180 and row['psi'] == -180:
        print(f"Rotamer {row['rotamers']} ma prawdopodobieństwo: {row['prob']:.4f} (Kąt CHI1: {row['chi_vals'][0]}°)")
        break # Przerywamy po znalezieniu pierwszego dopasowania testowego
