# Avaliacao 3 - Replicacao de dados

## Etapas 6 e 7: eleicao automatica e reconciliacao de logs

Aplicacao .NET 10 com quatro instancias do mesmo servidor gRPC e um cliente.
Um lider eleito recebe escritas; os seguidores buscam o log via `Fetch` e mantem
seus proprios arquivos JSON. O log continua separado dos dados comitados.

O lider exige **quorum fixo de tres nos (ele mesmo e dois seguidores)** antes
de comitar uma escrita e responder sucesso ao cliente. Atividade e confirmacao
de armazenamento sao acompanhadas separadamente. A eleicao usa termos e voto
unico persistidos, maioria de tres e verificacao da atualizacao do log.

### Executar

Requer SDK .NET 10. Na raiz do repositorio, compile antes de iniciar os processos:
Encerre os nos e o cliente da versao anterior antes de compilar; no Windows,
os executaveis abertos podem bloquear a substituicao das DLLs. Reinicie todos
com a mesma versao, sem misturar etapas.

```powershell
dotnet build avaliacao-3\Replication.slnx
```

Execute cada comando em um terminal separado, mantendo os quatro abertos.
Em cada terminal dos servidores, defina o mesmo mapa de membros:

```powershell
$peers = "node-1=http://127.0.0.1:7001,node-2=http://127.0.0.1:7002,node-3=http://127.0.0.1:7003,node-4=http://127.0.0.1:7004"
```

Os caminhos `cluster` abaixo evitam reutilizar estados antigos sem log.
Execute somente o comando correspondente a cada terminal:

```powershell
dotnet run --no-build --project avaliacao-3\src\Replication.Node -- --node-id node-1 --peers $peers --port 7001 --data avaliacao-3\data\cluster\node-1\state.json
```

```powershell
dotnet run --no-build --project avaliacao-3\src\Replication.Node -- --node-id node-2 --peers $peers --port 7002 --data avaliacao-3\data\cluster\node-2\state.json
```

```powershell
dotnet run --no-build --project avaliacao-3\src\Replication.Node -- --node-id node-3 --peers $peers --port 7003 --data avaliacao-3\data\cluster\node-3\state.json
```

```powershell
dotnet run --no-build --project avaliacao-3\src\Replication.Node -- --node-id node-4 --peers $peers --port 7004 --data avaliacao-3\data\cluster\node-4\state.json
```

Em um quinto terminal:

```powershell
dotnet run --no-build --project avaliacao-3\src\Replication.Client -- --server http://127.0.0.1:7001 --peers "http://127.0.0.1:7001,http://127.0.0.1:7002,http://127.0.0.1:7003,http://127.0.0.1:7004"
```

O endereco padrao do cliente e `http://127.0.0.1:7001`, e a porta padrao do
servidor e `7001`. `--peers` habilita a eleicao; todos iniciam como seguidores,
sem prioridade para node-1. O mapa deve conter quatro identidades/endpoints
locais distintos, incluindo o proprio no com a porta de `--port`.
Nao misture `--peers` com `--role` ou `--leader`.
Cada instancia deve usar porta e arquivo diferentes. Os nomes dos membros
sao extraidos do mapa; se tambem usar `--members`, as listas devem coincidir.
Sem `--peers`, os comandos anteriores de `--role leader|follower` e `--leader`
continuam disponiveis para demonstrar os passos 4/5, sem eleicao. Nao misture
esse modo com o cluster automatico nem altere a lideranca fixa manualmente.
Identidades desconhecidas e repeticoes da mesma identidade nao aumentam o quorum.
Esta etapa assume nos confiaveis: nao implementa autenticacao de identidades.
O servidor aceita apenas conexoes locais IPv4, via HTTP/2
sem TLS (h2c), para desenvolvimento. Nao exponha essa configuracao na Internet.
O terminal do servidor exibe logs; o menu agora fica somente no cliente:

- `1`: informar a chave e depois o valor; uma chave existente e atualizada.
- `2`: consultar uma chave.
- `3`: listar os dados comitados em ordem de chave.
- `4`: inspecionar log, commit e, no lider, quorum, nos ativos e indices
  confirmados por seguidor. O seguidor nao estima o quorum global.
- `0`: sair.

As chaves diferenciam maiusculas de minusculas e nao podem ser vazias ou conter
somente espacos. Os valores podem ser vazios. As entradas sao lidas como texto,
uma linha por campo.

O caminho informado ao servidor e relativo ao diretorio de execucao. Sem `--data`, o arquivo
fica em `data\state.json` dentro do diretorio do executavel. O caminho absoluto
e exibido na inicializacao. Use o mesmo caminho para recuperar os dados.

### Verificar a recuperacao apos reinicio

1. Inicie os quatro servidores e o cliente nos terminais separados.
2. No cliente, escolha `1`, informe `produto:1` e depois `Teclado`.
3. Escolha `2` e consulte `produto:1`; escolha `3` para listar.
4. Abra um cliente com `--server http://127.0.0.1:7002` e consulte/lista os dados.
   A convergencia e eventual, nao necessariamente imediata apos a escrita.
5. Escolha `4` em cada no para ver seu papel, log e commit.
6. Identifique um seguidor com `4`, encerre-o com `Ctrl+C`, faca novas escritas
   e reinicie-o com o mesmo comando. Ele busca as entradas faltantes.
7. Com os quatro ativos e sincronizados, encerre o lider com `Ctrl+C`.
   Os tres sobreviventes elegem substituto. Verifique novo papel e termo com `4`.
8. Grave novamente: o cliente consulta os endpoints de `--peers` e direciona
   a escrita ao lider atual. Read/List/GetLog continuam locais ao `--server`;
   se esse no estiver desligado, use outro `--server` para essas consultas.
9. Reinicie o antigo lider com o MESMO JSON. Se o log for compativel, ele
   reconhece o termo atual, permanece seguidor e recupera seu atraso. Se houver
   cauda divergente nao comitada, a replicacao recua ate o prefixo comum e a substitui.

### Como funciona a eleicao

O [ElectionState](src/Replication.Node/Replication/ElectionState.cs) controla
os papeis `seguidor`, `candidato` e `lider`. Cada no sorteia um timeout entre
1200 e 2000ms. Sem resposta valida do lider, incrementa e persiste o termo,
vota em si mesmo e solicita votos via `RequestVote`. Sao necessarios **tres
votos distintos de quatro**. Empates/falta de maioria geram novas tentativas
com timeout aleatorio. Dois nos sozinhos nao elegem um novo lider.

Um voto so e concedido uma vez por termo, e apenas para log pelo menos tao
atualizado: compara primeiro o termo da ultima entrada e depois seu indice.
Termo maior observado em requisicao ou resposta provoca persistencia e retorno
a seguidor, inclusive se a resposta pertencer a uma eleicao antiga.
Votos/respostas atrasados nao restabelecem lideranca de um termo anterior.
Descobrir um endereco nao renova o timeout; so Fetch valido do lider ou voto
concedido o fazem, alem de iniciar/observar um novo termo.

O [AutomaticClusterWorker](src/Replication.Node/Replication/AutomaticClusterWorker.cs)
consulta `GetLeader` nos membros conhecidos quando nao sabe quem e o lider,
e entao faz Pull a cada 100ms. RPCs entre nos tem deadline de 350ms.
Nao ha heartbeat enviado pelo lider: as respostas ao Fetch mantem o vinculo.
Tempos sao configurados para demonstracao local, nao constituem SLA de failover.

Ao mudar de termo, o coordenador descarta confirmacoes antigas. Novas escritas
usam o termo atual. O commit por maioria so avanca diretamente por uma entrada
**do termo atual**, incluindo o prefixo anterior. Nao ha entrada no-op:
pendencias de termos anteriores podem aguardar a primeira escrita do novo lider,
mesmo que ja estejam em tres discos. Perder lideranca durante uma escrita
interrompe a espera sem confirmar sucesso e sem descartar a entrada.
Um lider isolado pode continuar exibindo esse papel ate observar termo maior,
mas sem quorum nao aceita novas escritas nem comita entradas.

O [ClusterWriter](src/Replication.Client/ClusterWriter.cs) descobre o lider antes
de cada escrita, por GetLog e indicacoes dos seguidores. Com `--peers`, consegue
descobrir o substituto mesmo se o `--server` estiver desligado. Sem essa lista,
depende do servidor escolhido e de sua indicacao. A descoberta e limitada,
recusa lider desconhecido/ambiguo no termo mais recente e nao repete Write
automaticamente. Se a lideranca mudar entre descoberta e escrita, a chamada
pode falhar; consulte a chave antes de repetir.

### Demonstrar perda e recuperacao de quorum

1. Com os quatro nos ligados, use `4` no cliente conectado ao lider para
   conferir `quorum disponivel`, e grave um dado com a opcao `1`.
2. Desligue um seguidor: com lider e outros dois seguidores, escritas funcionam.
3. Desligue outro seguidor e aguarde mais de 600ms. Agora so ha dois nos ativos.
4. Confira `quorum indisponivel`. Uma escrita nova retorna `Unavailable`
   sem adicionar uma entrada ao log. Leituras comitadas continuam funcionando.
5. Reinicie um seguidor com o mesmo JSON. Apos os Fetchs restabelecerem o quorum,
   escritas voltam a funcionar. O outro pode voltar depois e recuperar seu atraso.

O [QuorumCoordinator](src/Replication.Node/Replication/QuorumCoordinator.cs)
mantem o ultimo Fetch e o ultimo indice confirmado de cada membro na memoria
do lider. Mais de 600ms sem Fetch valido torna a replica inativa; um monitor
verifica isso a cada 100ms, e toda nova escrita tambem consulta o tempo atual.
Ao reiniciar o lider, as replicas precisam se apresentar novamente: atividade
antiga nao e recuperada do JSON.

Uma escrita aceita e primeiro persistida no log SEM commit. O indice de commit
so avanca quando dois seguidores distintos informam indices que incluem essa
entrada, alem da copia local do lider. Entregar uma resposta Fetch nao e prova
de armazenamento. O commit e sua aplicacao sao persistidos antes de liberar a
resposta de sucesso ao cliente. Escritas concorrentes aguardam seus proprios
indices sem bloquear a chegada de Fetchs.

Sem quorum inicial, a escrita e recusada sem alterar o log. Se o quorum se perder
durante a espera, a chamada falha; se os nos estiverem ativos mas atrasados,
o servidor espera no maximo cinco segundos e retorna `DeadlineExceeded`.
O deadline do cliente pode encerrar a chamada antes. Uma entrada ja persistida
permanece pendente e pode ser comitada quando as replicas voltarem. Falha ou
timeout nao significa cancelamento definitivo da operacao: nao ha rollback
nem repeticao automatica. Confira o log e a chave antes de repetir.
Falha de persistencia do commit impede sucesso e desabilita novas escritas
ate a correcao do armazenamento e reinicio do lider.

### Fluxo Pull

Os workers usam `BackgroundService`, canais gRPC reutilizados e `PeriodicTimer`
de 100ms. O worker automatico tambem executa descoberta/eleicao; o
[PullReplicationWorker](src/Replication.Node/Replication/PullReplicationWorker.cs)
preserva o modo antigo de lider fixo, cujo Fetch tem deadline de 2s.
Indisponibilidade/timeout gera aviso e novas tentativas. Leituras locais
continuam disponiveis.

1. Seguidor informa identidade, ultimo indice e termo persistidos.
2. Lider valida a posicao e retorna ate 128 entradas e seu indice de commit.
3. Seguidor persiste o lote SEM aplicar os valores.
4. No proximo Fetch, confirma a posicao que ja esta em disco.
5. Aplica somente o prefixo confirmado nessa requisicao, limitado pelo commit
   retornado pelo lider. Entradas recem-recebidas aguardam o proximo Fetch.

O lider registra cada posicao confirmada no Fetch e calcula o terceiro maior
indice entre sua copia local e as replicas ativas; esse e o limite do commit.

O lider nao abre conexoes nem envia dados espontaneamente aos seguidores.
Nao ha dependencia de RabbitMQ, Docker, Redis ou Kubernetes.
Os logs exibem papel, termo e progresso. Falhas permanentes de protocolo,
estado ou persistencia **locais** sao registradas e interrompem o host.
Erros retornados por um peer remoto (por exemplo `Internal` ao persistir um
voto) sao apenas registrados: o no continua ativo e trata o peer como
indisponivel naquela rodada. A gravacao do JSON repete algumas vezes, por
ate ~700ms, quando o Windows bloqueia temporariamente o arquivo (antivirus,
indexador ou editor com o `state.json` aberto).
Com menos de tres nos ativos nao ha maioria: o termo cresce a cada timeout
sem lider eleito, o que e esperado. Ao reunir os nos, todos adotam o maior termo.
Divergencia de log no modo automatico e registrada sem encerrar o host:
ele mantem leituras do estado ja comitado, mas nao confirma essa cauda ao lider.

### Contrato e tratamento de erros

O [storage.proto](src/Replication.Contracts/Protos/storage.proto) define os RPCs:

| RPC | Entrada | Resultado |
| --- | --- | --- |
| `Write` | Chave e valor | Indice do log e indices de commit/aplicacao, apos persistir e comitar |
| `Read` | Chave | Valor, ou status `NotFound` |
| `List` | Requisicao vazia | Entradas ordenadas por chave |
| `GetLog` | Requisicao vazia | Log, indices, papel e estado de quorum/progresso das replicas no lider |
| `ReplicationTransport.Fetch` | Identidade, termo atual, ultimo indice/termo do log | Termo atual, aceitacao/conflito, lote e commit do lider |
| `ReplicationTransport.RequestVote` | Termo, candidato e ultima posicao/termo do log | Termo atual e voto concedido/negado |
| `ReplicationTransport.GetLeader` | Requisicao vazia | Termo, identidade conhecida do lider e papel do no consultado |

O cliente le e lista somente o estado aplicado. `GetLog` e apenas para
inspecao: revela quais entradas estao no log e se ja foram comitadas.

O projeto `Replication.Contracts` gera as mensagens, a base do servidor e o
cliente gRPC a partir do contrato. O servidor usa ASP.NET Core gRPC; o cliente
usa `Grpc.Net.Client`. Nao ha REST ou acesso do cliente ao arquivo JSON.

Chaves invalidas sao rejeitadas tambem no servidor com `InvalidArgument`.
Valor vazio e permitido e nao se confunde com chave inexistente.
Falhas de persistencia retornam `Internal`, sem confirmar sucesso nem aplicar
entradas cujo commit falhou; os detalhes ficam nos logs do servidor.
Ausencia de quorum retorna `Unavailable`, com explicacao no cliente.

Cada chamada do cliente tem deadline de cinco segundos, configuravel:

```powershell
dotnet run --project avaliacao-3\src\Replication.Client -- --server http://127.0.0.1:7001 --timeout-seconds 10
```

O cliente informa chave inexistente, servidor indisponivel e timeout, mantendo
o menu disponivel. `Ctrl+C` cancela uma chamada em andamento. Nao ha repeticao
automatica: se a comunicacao falhar durante uma escrita, o servidor pode ja
ter persistido o dado. Consulte a chave antes de repetir.

### Formato persistido

```json
{
  "formatVersion": 2,
  "currentTerm": 1,
  "votedFor": "node-2",
  "commitIndex": 1,
  "lastApplied": 1,
  "log": [
    {
      "index": 1,
      "term": 1,
      "key": "produto:1",
      "value": "Teclado"
    }
  ],
  "data": {
    "produto:1": "Teclado"
  }
}
```

`log` guarda as operacoes persistidas, inclusive as ainda nao comitadas.
`commitIndex` e o maior indice efetivado; `lastApplied` indica ate onde o estado
final foi atualizado. `data` contem somente os valores ja aplicados. Uma entrada
com indice maior que `commitIndex` nao aparece em `Read` nem em `List`.

O armazenamento fornece `AppendUncommitted`, que persiste a entrada sem alterar
`data`, e `CommitThrough`, que aplica em ordem as entradas ate o indice indicado.
A aplicacao de dados, `commitIndex` e `lastApplied` e salva em uma substituicao
atomica do JSON. Um erro ao persistir o commit nao torna a entrada visivel. A
operacao gRPC `Write` persiste a entrada e aguarda confirmacoes nos Fetchs.
O coordenador de quorum chama `CommitThrough` apos obter tres copias; a resposta
de sucesso so e liberada depois de persistir esse commit.

`currentTerm` e `votedFor` sao persistidos antes de solicitar/conceder votos
ou responder em um termo maior, junto do mesmo snapshot de log e dados.
Reiniciar nao permite votar em outro candidato no mesmo termo.
JSONs da etapa anterior sem esses campos iniciam com termo `0` e voto nulo;
se houver entradas com termo maior, a inicializacao automatica normaliza o termo.
Entradas usam indice sequencial e o termo em que foram criadas. `GetLog`
mostra tambem o termo atual do no, que pode ser maior que o da ultima entrada;
os testes cobrem entrada nao comitada persistida, invisivel apos reinicio, e sua
aplicacao depois de `CommitThrough`.

O formato anterior, que continha apenas `data`, e carregado preservando os
valores existentes. Como esse formato nao registrava historico, o log comeca
vazio apos a migracao, e novas operacoes recebem indices a partir de `1`.
Esses dados legados nao podem ser replicados apenas pelo log. Para o cluster,
use novos caminhos como os exemplos acima e reinsira os dados pelo cliente.
Nao apague seu arquivo antigo. O Fetch rejeita explicitamente estado comitado
que nao possa ser reproduzido pelo log. Nao reutilize arquivos de clusters
diferentes.

Na eleicao automatica, o Fetch compara o indice e o termo do prefixo informado
pelo seguidor. Se divergirem, o lider informa um indice de conflito e o seguidor
recua a verificacao ate encontrar um prefixo comum. A partir dele, substitui
atomicamente somente a cauda nao comitada e recebe os lotes seguintes (ate 128
entradas por chamada). Um log menor no novo lider tambem remove uma cauda local
nao comitada. A reconciliacao nunca trunca entradas comitadas: se nao for possivel
validar o prefixo ate o `commitIndex` local, o seguidor preserva seus dados,
rejeita aquele lider e aguarda nova eleicao. O modo de lider fixo continua
rejeitando logs divergentes. Esta etapa nao e uma implementacao Raft completa e
pronta para producao.

O armazenamento usa `System.Text.Json`, sem banco de dados ou dependencias
externas de persistencia. A logica fica em
[JsonKeyValueStore](src/Replication.Node/Storage/JsonKeyValueStore.cs),
separada do [servico gRPC](src/Replication.Node/Services/StorageService.cs) e do
[menu do cliente](src/Replication.Client/Program.cs).

Cada atualizacao grava uma nova versao completa em um arquivo `.tmp`, faz
flush para disco e substitui o JSON oficial no mesmo diretorio. A memoria so e
atualizada depois da substituicao. Isso evita sobrescrever parcialmente o
arquivo oficial; nao constitui garantia absoluta contra perda de energia ou
falhas de hardware. O custo de reescrever todos os dados e adequado a esta etapa.

Um arquivo `.lock`, mantido aberto com acesso exclusivo, impede duas instancias
de usarem simultaneamente o mesmo estado. Ele pode permanecer no disco apos a
saida: o bloqueio e liberado ao fechar o processo, nao ao apagar o arquivo.
Cada futuro no deve usar seu proprio caminho.

JSON invalido interrompe a inicializacao sem apagar os dados. Falhas de escrita
sao registradas e retornadas ao cliente sem confirmar sucesso. Um `.tmp` abandonado
nao e promovido automaticamente; a proxima escrita o substitui.
Os dados da pasta local `data` nao devem ser versionados.

### Compilar e testar

```powershell
dotnet build avaliacao-3\Replication.slnx
dotnet test avaliacao-3\tests\Replication.Tests
```

Para validar em Release, acrescente `--configuration Release` aos comandos.
Ao executar com `--no-build`, use a mesma configuracao usada na compilacao.

Os testes locais cobrem gravacao, recuperacao, migracao do formato anterior,
entrada nao comitada, commit e aplicacao, validacao, exclusividade, falha de
persistencia e concorrencia. Os testes gRPC iniciam um servidor real em porta
dinamica e verificam os RPCs, indices reportados, leitura de somente dados
comitados, visibilidade do log, reinicio, falhas e indisponibilidade.
Um transporte atrasado de teste verifica o deadline sem introduzir atrasos
artificiais no servidor de producao.
Os testes Pull iniciam quatro hosts com portas dinamicas e arquivos separados,
verificam convergencia, bloqueio de escrita no seguidor, recuperacao do seguidor,
retomada apos reinicio do lider, lotes, confirmacao de persistencia e isolamento
de entradas nao comitadas. Tambem validam lotes invalidos e logs divergentes.
Os testes de quorum verificam confirmacoes distintas, espera da resposta gRPC,
expiracao de heartbeat com relogio controlado, rejeicao sem adicionar ao log,
timeout, cancelamento e falha de persistencia. A integracao verifica perda e
recuperacao do quorum com quatro hosts gRPC e JSONs independentes.
Os testes de eleicao verificam persistencia de termo/voto, voto unico,
atualizacao do log, timeout com relogio controlado, democao por termo maior,
limpeza das confirmacoes e regra de commit do termo atual. Quatro hosts gRPC
reais validam eleicao, failover com tres sobreviventes, descoberta/redirecionamento
do cliente e recuperacao de cauda divergente sem remover o prefixo comitado.
Dois hosts nao elegem lider nem aceitam escrita.
O [NuGet.Config](NuGet.Config) limita a restauracao desta avaliacao ao nuget.org,
incluindo os pacotes de teste, sem alterar a configuracao das outras avaliacoes.
A regra existente em [.gitignore](../.gitignore) ignora `avaliacao-3/tests/**`;
os testes permanecem locais e essa escolha de versionamento foi preservada.
