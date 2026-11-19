# Předání další etapy: blokový svět

**Stav po druhé etapě:** malé seedované terasy, viditelné stěny bloků, shodná geometrie kolizí, omezený streaming a ovládání postav jsou nyní implementované v C#. Aktuální důkazy a omezení jsou v `../artifacts/owned-handoff.md` a `world-qa.json`. Následující text zachovává původní plán první etapy.

Začít až po prohlídce postav. Veškerá runtime logika zůstane v C#.

1. Deterministický seed a datové chunky (např. 16×16×64), oddělené od scény a bez návaznosti na školní data.
2. Jeden malý povrchový biom, jednoduchá výška a pevné bloky. Vytvářet pouze viditelné stěny; sousední chunky sdílejí hraniční vzorky, žádné vnitřní stěny mezi plnými bloky.
3. Mesh a kolize ze stejné verze chunkových dat. Vlastní kontrola usazení přes skutečný raycast a render musí ověřit spawn i švy chunků.
4. Omezené streamování kolem hráče, fronta úloh s rozpočtem na snímek; data lze připravovat mimo hlavní vlákno, scénové uzly přidávat bezpečně na hlavním vlákně.
5. Nejprve měřit desktop. Po skutečném mobilním testu určit počet aktivních chunků, trojúhelníky/drawcalls, paměť a dobu generování; používat atlas a omezené materiály. Žádný slib mobilního výkonu bez měření.

V první malé scéně ověřit opakovatelnost seedu, švy, kolize a uvolňování chunků. Multiplayer, účty, školní brána, crafting a boj sem nepatří. V této dodávce generátor nebyl zahájen.
