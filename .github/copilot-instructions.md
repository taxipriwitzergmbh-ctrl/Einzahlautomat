# Copilot Instructions

## General Guidelines
- First general instruction
- Second general instruction

## Logging and Event Handling
- Wenn der Nutzer Logzeilen wie '(wiederholt 1x)' zeigt, kann das in dieser Codebasis eine reale doppelte Auszahlung/Device-Event bedeuten und nicht nur Logger-Deduplizierung; daher bei Ursachenanalyse erst AppLogger-Implementierung prüfen. Im Kontext des SmartCoin-Logs ist „(wiederholt 1x)“ als echte zusätzliche Auszahlung zu interpretieren (mindestens im auftretenden Szenario), nicht nur als Logger-Dedupe.