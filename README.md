# Battery Tray Monitor

Aplicativo nativo de bandeja para Windows, em C# / .NET 10 e WinForms. Não usa navegador, Electron, WebView, servidor próprio, serviço separado ou banco de dados. As consultas usam APIs do Windows e relatórios HID de leitura. Há também uma integração opcional com o **G HUB em execução**, para obter os percentuais calculados pela Logitech, inclusive do G502 LIGHTSPEED e G935.

## Executar

Windows 10 **1809 (build 17763) ou posterior**, ou Windows 11. A publicação entregue é x64. Não é necessário executar como administrador.

**[Baixar Battery Tray Monitor para Windows x64](https://github.com/jaovborges/battery-tray-monitor/releases/latest/download/BatteryTrayMonitor-win-x64.zip)**

1. Extraia o ZIP e abra `BatteryTrayMonitor.exe`. O pacote inclui o runtime do .NET. O SDK é necessário apenas para compilar o código-fonte.
2. O aplicativo inicia na bandeja. O Windows pode colocar o ícone no menu de ícones ocultos, acessível pela seta ao lado do relógio.
3. Clique com o botão direito no ícone de controle e escolha **Mostrar dispositivos**. Marque **Bandeja** nos dispositivos desejados.
   Para **G502 LIGHTSPEED e G935**, mantenha o G HUB em execução e a opção **Usar níveis do Logitech G HUB** ativada na aba Geral (padrão). A coluna Fonte deve mostrar **Logitech G HUB**. O monitor exibe o percentual fornecido por ele, arredondado para inteiro; os detalhes preservam o valor decimal e o estado de carregamento.
4. Cada dispositivo selecionado recebe um ícone próprio. O padrão é somente número. `?` significa bateria indisponível; `–` significa desconectado. Não se mantém um percentual antigo como se fosse atual.
5. Clique esquerdo abre o resumo flutuante; clique direito abre painel, atualização, configurações, inicialização e saída. Fechar o painel mantém o monitoramento. **Sair** remove os ícones e encerra o processo.

O ícone de controle do aplicativo permanece disponível mesmo sem dispositivos selecionados. O painel aparece na barra de tarefas somente enquanto estiver aberto. O Windows controla a posição, o agrupamento e a visibilidade final dos ícones; arraste-os na bandeja para ajustar sua disposição. A ordem interna fica salva, mas o aplicativo não força a ordem do Explorer.

Parâmetros opcionais:

```powershell
.\BatteryTrayMonitor.exe --panel
.\BatteryTrayMonitor.exe --tray
.\BatteryTrayMonitor.exe --diagnostics "C:\caminho\diagnostico.json"
```

`--panel` força abrir o painel na primeira instância. Uma segunda instância normal termina imediatamente. O diagnóstico é um processo transitório de consulta, sem monitoramento contínuo; execute-o depois de sair do monitor para evitar consultas HID simultâneas. O arquivo fica somente no caminho escolhido e inclui nomes, identificadores locais e respostas do protocolo; não há envio para servidores.

## Métodos de leitura e limites

| Provedor | Implementação | Limites |
| --- | --- | --- |
| WindowsBatteryProvider | Enumera Bluetooth clássico emparelhado com `Windows.Devices.Enumeration`; consulta `System.Devices.BatteryLife` e propriedades PnP via SetupAPI, incluindo a chave `{104EA319-6EE2-4701-BD47-8DDBF425BBE5}, 2`. | Depende de o driver publicar a bateria. A propriedade PnP é usada como melhor esforço, sem garantia universal. O horário mostrado é o da consulta, não o instante da medição interna do driver. Bluetooth clássico não tem um serviço GATT universal; headsets só funcionam quando o Windows/driver expõe o valor. |
| BluetoothBatteryProvider | Enumera BLE emparelhado; quando conectado, lê diretamente Battery Service `180F`, Battery Level `2A19`, com leitura sem cache. | Requer serviço exposto, acesso permitido pelo Windows e dispositivo conectado. Um serviço pode ser inexistente, protegido ou inacessível. Cada tentativa tem limite de 8 segundos. Não faz emparelhamento automático. Aparelhos com várias baterias apresentam a primeira característica de bateria válida. |
| LogitechBatteryProvider | Transporte HID via HidSharp. Detecta coleções de fabricante USB Logitech com relatórios longos HID++. Faz ping HID++ 2.0, descoberta de funções e nome/tipo; consulta `1004` (somente quando anuncia suporte a percentual) e `1000` (percentual em descarga ou estado explicitamente completo). Consulta `1001` e `1F20` para informar tensão nos detalhes. | Suporte **parcial**, dependente de modelo e firmware. Slots de receptor 1–6 e endpoint direto FF. HID++ 1.0, relatórios curtos exclusivos, outros fabricantes e protocolos proprietários sem especificação não são implementados. Algumas coleções podem estar ocupadas por outro aplicativo. Nunca converte tensão ou categorias qualitativas em percentual. |
| GHubBatteryProvider | Cliente WebSocket de `ws://127.0.0.1:9010`, canal local já fornecido pelo G HUB. Consulta somente `GET /devices/list` e `GET /battery/<id>/state`. | **Validado com G502 LIGHTSPEED e G935 nesta máquina.** Exige G HUB em execução. Interface não oficial, sujeita a mudanças da Logitech. O monitor não cria servidor, não modifica configurações do G HUB, não injeta código e não lê processos, memória ou bancos privados. |

Para Logitech por Bluetooth, são usados os provedores Windows/GATT. Para **Unifying, Bolt e LIGHTSPEED**, a tentativa independente usa HID++ 2.0 quando o receptor disponibiliza a coleção e o aparelho responde ao protocolo implementado. Isso não equivale a compatibilidade garantida com todos esses receptores: Unifying e Bolt **não foram validados com hardware nesta entrega**. Options+ não é consultado. O provedor G HUB usa uma interface não oficial que foi testada localmente, com cancelamento e timeouts; se o G HUB fechar ou deixar de responder, seus percentuais são descartados na próxima consulta e os provedores independentes continuam funcionando.

As identidades Bluetooth usam endereço físico quando disponível, com fallback para ContainerId/identificador Windows. Fontes com a mesma identidade são combinadas; leitura conectada e disponível vence. G HUB tem prioridade sobre HID++/GATT, que têm prioridade sobre a propriedade do driver. G HUB e HID++ usam ContainerId do Windows e slot do receptor para correlacionar o mesmo aparelho; os IDs transitórios `dev000000xx` do G HUB não são persistidos. As preferências dos identificadores HID anteriores são migradas quando correlacionadas. Não se usa apenas nome para juntar aparelhos diferentes. Identificadores distintos que o Windows não permita correlacionar podem permanecer separados; mudança de endereço BLE, porta/receptor ou novo emparelhamento pode exigir selecionar novamente. Trocar o aparelho emparelhado no mesmo slot requer revisar a seleção.

O aplicativo consulta apenas periféricos Bluetooth e HID Logitech compatíveis; não lê a bateria do notebook.

## Validação real realizada em 18/11/2026

- Build Release x64: sem erros ou avisos.
- Suite automatizada: **152 verificações aprovadas**, incluindo limites de cores, JSON, corrupção de configuração, deduplicação, alertas com histerese, correlação/decodificação HID++, validação de respostas G HUB, isolamento de falhas, remoção de dispositivos, tamanho do tooltip e renderização de ícones em 16/20/24/32/48 px. Uma sequência de 1.000 mudanças de ícone não apresentou crescimento de handles GDI/USER.
- Interface executada e inspecionada no Windows: painel, aparência, seleção para bandeja e persistência. Fechar o painel manteve o processo em execução.
- Executável independente final iniciado e inspecionado; seleção para bandeja e persistência verificadas. Na versão com G HUB, o painel mostrou G935 a **92%** e G502 a **49%**, ambos com fonte **Logitech G HUB**, sem entradas duplicadas. Ambos foram selecionados para a bandeja. Abrir uma segunda instância na validação inicial retornou código 0 e manteve somente um processo. Tamanhos atuais: independente **55,43 MiB**; dependente do runtime **24,71 MiB**.
- **G502 LIGHTSPEED Wireless Gaming Mouse**, receptor `046D:C539`: ping, identificação e tensão por HID++ `1001` confirmados. O aparelho não anunciou `1000`/`1004`. **A integração G HUB retornou 49%**, com leitura ativa e sem carregamento, na validação real. Sem G HUB, a leitura direta continua informando somente tensão.
- **G935 Gaming Headset**, `046D:0A87`: ping, identificação e tensão por HID++ `1F20` confirmados. **A integração G HUB retornou 92,05%, carregando** (92 no ícone). Sem G HUB, a leitura direta continua sem percentual.
- Dispositivos Bluetooth emparelhados e desconectados foram enumerados e exibidos sem bateria inventada.
- Percentuais fornecidos pelo G HUB foram validados nos dois Logitech acima, com associação de identidade entre as fontes. Os caminhos de percentual direto GATT/HID++ continuam exigindo outro hardware compatível para validação física. Os valores do G HUB são os cálculos fornecidos pelo software Logitech; o monitor não afirma que sejam uma medição física exata da carga nem calcula sua própria curva de tensão.
- Não foram realizados ensaio de vários dias, teste físico de retirada de receptor, validação de notificações pelo shell nem reinício real do Explorer. A implementação libera recursos, trata desconexão e escuta `TaskbarCreated`; esses cenários ainda devem ser verificados no ambiente de uso.

Na sessão de desenvolvimento, a execução por SDK após abertura do painel e consultas ficou próxima de **115 MiB de working set**. O executável independente comprimido com integração G HUB e os dois ícones apresentou aproximadamente **217 MiB de working set** na conferência final. Esses valores incluem WinForms, WinRT, runtime e carregamento do executável; variam com hardware/Windows e não demonstram estabilidade por vários dias. Não se promete consumo de poucos megabytes. Publicação dependente reduz tamanho em disco, não elimina o custo do runtime em memória.

## Preferências, eventos e desempenho

- Configurações em `%AppData%\BatteryTrayMonitor\settings.json`, com gravação por arquivo temporário e substituição. JSON inválido retorna aos padrões. Seleção, ordem, metadados dos dispositivos, cores, faixas, fonte, modo, intervalos e notificações são persistidos.
- Inicialização automática via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, sem administrador. O registro é a fonte efetiva da opção; JSON guarda o estado ao salvar. Mantenha o executável em um caminho fixo antes de ativar essa opção.
- Intervalo padrão de 5 minutos, opções 30 s / 1 / 5 / 10 / 15 min. Consultas assíncronas, sem ciclos simultâneos. Provedores têm cancelamento e orçamento de 45 segundos, além dos limites HID/GATT por operação.
- Watchers Bluetooth e mensagens de alteração de hardware agendam atualizações com debounce. O intervalo também serve como fallback. Não há conexão GATT permanente nem assinatura permanente de notificações de bateria; leituras de bateria ocorrem no intervalo ou na atualização manual.
- G HUB também é consultado no intervalo escolhido (5 minutos por padrão; selecione 30 segundos se desejar atualização mais frequente). Não há WebSocket permanente ou assinatura de eventos do G HUB. Conexão tem timeout de 3 segundos e cada solicitação tem timeout de 4 segundos; mensagens têm limite de 1 MiB. Valor ausente, fora de 0–100, bateria removida, erro de carregamento ou dispositivo inativo não viram percentual válido. Um estado antigo pode permanecer até a próxima consulta, como nas demais fontes.
- Notificações usam balões nativos de `NotifyIcon`, sujeitos a Não Perturbe e configurações do Windows. Uma bateria só rearma o alerta após subir pelo menos 5 pontos acima do limite. Desconectar/reconectar com bateria ainda baixa não repete continuamente o alerta. A memória de alertas dura a sessão, portanto reiniciar pode gerar novo alerta.
- Ícones são recriados somente quando conteúdo, aparência, tema ou DPI mudam. HICON, fontes, pincéis, bitmaps, streams HID e serviços GATT têm descarte explícito. Não há log contínuo em disco.
- Tema e DPI são reavaliados nas atualizações; alterações do Windows disparam atualização quando notificadas pelo shell. A adaptação de contraste pode clarear/escurecer a cor escolhida; desative essa opção para usar o hexadecimal exatamente.

## Compilar e testar

Instale o **SDK .NET 10 x64**. O SDK local usado nesta sessão está em `%LocalAppData%\BatteryTrayBuild\dotnet`; o script de publicação o reconhece automaticamente. Se o `dotnet` do PATH não encontrar SDK, use esse executável diretamente.

```powershell
dotnet restore BatteryTrayMonitor.sln
dotnet build BatteryTrayMonitor.sln -c Release
dotnet run --project tests\BatteryTrayMonitor.Tests -c Release
dotnet run --project src\BatteryTrayMonitor -c Release -- --panel
```

A suite usa um executável de verificações sem framework de testes adicional. Falhas retornam código diferente de zero. Os dispositivos artificiais existem exclusivamente nos testes, nunca no aplicativo final.

## Publicar

```powershell
.\publish.ps1
.\publish.ps1 -FrameworkDependent
.\publish.ps1 -Runtime win-arm64
```

O padrão gera `artifacts/win-x64-standalone/BatteryTrayMonitor.exe`, arquivo único comprimido com runtime incluído, sem instalador. A alternativa em `artifacts/win-x64-framework-dependent` exige **.NET Desktop Runtime 10** compatível com a arquitetura. O script também aceita x86 e ARM64; só x64 foi compilado/executado nesta entrega. Não se usa trimming: WinForms/WinRT usam mecanismos incompatíveis com remoção indiscriminada de código. O tamanho independente inclui o runtime e é significativamente maior.

## Organização

```text
BatteryTrayMonitor.sln
src/BatteryTrayMonitor/
  Models/          identidade e combinação de leituras
  Providers/       contrato, Windows, BLE e Logitech/HID++
  Native/          acesso PnP com SetupAPI
  Monitoring/      atualização, cancelamento e estado de alertas
  Configuration/   JSON, preferências e inicialização
  UI/              painel, resumo, bandeja e renderização
tests/BatteryTrayMonitor.Tests/
publish.ps1
```

Para adicionar uma fonte, implemente `IBatteryProvider`, registre-a em `TrayApplication`, devolva apenas percentuais realmente conhecidos (ou `null`), informe identidade física estável e prioridade, e descarte recursos em `Dispose`.

## Referências técnicas e dependências

- [Microsoft: cliente Bluetooth GATT](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-client)
- [Microsoft: BluetoothLEDevice](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothledevice)
- [Microsoft: SetupDiGetDevicePropertyW](https://learn.microsoft.com/en-us/windows/win32/api/setupapi/nf-setupapi-setupdigetdevicepropertyw)
- [Especificação HID++ 2.0 da Logitech, cópia pública](https://lekensteyn.nl/files/logitech/logitech_hidpp_2.0_specification_draft_2012-06-04.pdf)
- [Driver HID++ do Linux: semântica das funções 1000, 1004, 1001 e 1F20](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-logitech-hidpp.c). Consultado para interpretação do protocolo; nenhum código do driver foi incorporado.
- [HidSharp](https://github.com/IntergatedCircuits/HidSharp), versão 2.6.4, Apache-2.0: única dependência de terceiros direta, usada para transporte HID e descoberta de coleções.
- [LGBattery: descrição do canal local do G HUB](https://github.com/bmrussell/LGBattery#lghub-api), referência do protocolo de consulta, validado com o G HUB instalado nesta máquina. Nenhum código do projeto foi incorporado.

Sem telemetria, conta, acesso à internet ou permissões administrativas durante o uso normal. O provedor G HUB comunica-se exclusivamente com o endereço de loopback local.
