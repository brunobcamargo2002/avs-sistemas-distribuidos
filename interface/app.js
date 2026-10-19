// URL da API do ms-principal. O front roda em servidor próprio, então a
// chamada é cross-origin — ajuste com VITE_API_URL se a porta mudar.
const API_BASE = import.meta.env?.VITE_API_URL ?? "http://localhost:5080";

const state = {
  products: [],
  cart: new Map(),
  category: "Todas",
  search: "",
  availableOnly: false,
  stream: null,
  // Pedido que acabou de ser criado e está esperando o link de pagamento.
  awaitingPaymentFor: null,
  // Aba aberta no clique de "Finalizar": o navegador só permite abrir durante
  // o gesto do usuário, então ela nasce em branco e recebe a URL depois.
  paymentTab: null,
};

const elements = {
  customerId: document.querySelector("#customer-id"),
  productGrid: document.querySelector("#product-grid"),
  productTotal: document.querySelector("#product-total"),
  categoryFilters: document.querySelector("#category-filters"),
  search: document.querySelector("#search-products"),
  availableOnly: document.querySelector("#available-only"),
  catalogMessage: document.querySelector("#catalog-message"),
  cartItems: document.querySelector("#cart-items"),
  cartCount: document.querySelector("#cart-count"),
  cartHeadingCount: document.querySelector("#cart-heading-count"),
  cartSubtotal: document.querySelector("#cart-subtotal"),
  submitOrder: document.querySelector("#submit-order"),
  orderFeedback: document.querySelector("#order-feedback"),
  ordersList: document.querySelector("#orders-list"),
  promotionForm: document.querySelector("#promotion-form"),
  promotionEmail: document.querySelector("#promotion-email"),
  promotionCategories: document.querySelector("#promotion-categories"),
  promotionOptions: document.querySelector("#promotion-category-options"),
  savePromotion: document.querySelector("#save-promotion-interest"),
  cancelPromotion: document.querySelector("#cancel-promotion-interest"),
  promotionFeedback: document.querySelector("#promotion-feedback"),
};

const currency = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });
const categoryNames = {
  hardware: "Hardware",
  perifericos: "Periféricos",
  notebooks: "Notebooks",
  redes: "Redes",
  software: "Software",
  acessorios: "Acessórios",
};

function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, (character) => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;",
  })[character]);
}

function categoryLabel(category) {
  const [group, detail] = category.split(".");
  if (!detail) return categoryNames[group] ?? group;
  return `${categoryNames[group] ?? group} · ${detail.replaceAll("_", " ")}`;
}

function renderPromotionCategories() {
  const selected = new Set([...elements.promotionOptions.querySelectorAll("input:checked")].map((input) => input.value));
  const categories = state.products.length
    ? [...new Set(state.products.map((product) => product.category))].sort()
    : Object.keys(categoryNames);
  elements.promotionOptions.innerHTML = categories.map((category) => `
    <label class="promotion-option">
      <input type="checkbox" name="categories" value="${escapeHtml(category)}" ${selected.has(category) ? "checked" : ""} />
      <span>${escapeHtml(categoryLabel(category))}</span>
    </label>`).join("");
}

async function submitPromotionInterest(cancel) {
  if (elements.promotionForm.getAttribute("aria-busy") === "true") return;
  if (!elements.promotionEmail.reportValidity()) return;
  const categories = [...elements.promotionOptions.querySelectorAll("input:checked")].map((input) => input.value);
  elements.promotionFeedback.classList.remove("error");
  if (!cancel && !categories.length) {
    elements.promotionFeedback.textContent = "Selecione ao menos uma categoria.";
    elements.promotionFeedback.classList.add("error");
    elements.promotionOptions.querySelector("input")?.focus();
    return;
  }

  const email = elements.promotionEmail.value.trim();
  elements.promotionForm.setAttribute("aria-busy", "true");
  elements.savePromotion.disabled = true;
  elements.cancelPromotion.disabled = true;
  elements.promotionEmail.disabled = true;
  elements.promotionCategories.disabled = true;
  elements.promotionFeedback.textContent = cancel ? "Cancelando inscrições..." : "Cadastrando interesses...";

  try {
    const response = await fetch(`${API_BASE}/api/promotion-interests`, {
      method: cancel ? "DELETE" : "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(cancel ? { email } : { email, categories }),
      signal: AbortSignal.timeout(15000),
    });
    if (!response.ok) {
      if ([404, 405, 501].includes(response.status)) {
        throw new Error("Serviço de interesses ainda indisponível. Nenhuma alteração foi confirmada.");
      }
      const result = await response.json().catch(() => ({}));
      throw new Error(result.detail ?? result.message ?? "Não foi possível concluir a solicitação.");
    }
    elements.promotionFeedback.textContent = cancel
      ? `Cancelamento solicitado para ${email}.`
      : `Cadastro solicitado para ${email}.`;
    if (cancel) {
      elements.promotionOptions.querySelectorAll("input").forEach((input) => { input.checked = false; });
    }
  } catch (error) {
    elements.promotionFeedback.textContent = error instanceof TypeError
      ? "Não foi possível acessar o serviço. Tente novamente mais tarde."
      : error.name === "TimeoutError"
        ? "O serviço demorou para responder. Não foi possível confirmar a solicitação."
        : error.message;
    elements.promotionFeedback.classList.add("error");
  } finally {
    elements.promotionForm.removeAttribute("aria-busy");
    elements.savePromotion.disabled = false;
    elements.cancelPromotion.disabled = false;
    elements.promotionEmail.disabled = false;
    elements.promotionCategories.disabled = false;
  }
}

elements.promotionForm.addEventListener("submit", (event) => {
  event.preventDefault();
  submitPromotionInterest(false);
});
elements.cancelPromotion.addEventListener("click", () => submitPromotionInterest(true));
renderPromotionCategories();

function filteredProducts() {
  const query = state.search.trim().toLocaleLowerCase("pt-BR");
  return state.products.filter((product) => {
    const matchesCategory = state.category === "Todas" || product.category.startsWith(state.category);
    const matchesSearch = !query || `${product.name} ${product.category}`.toLocaleLowerCase("pt-BR").includes(query);
    const matchesAvailability = !state.availableOnly || product.stock > 0;
    return matchesCategory && matchesSearch && matchesAvailability;
  });
}

function renderFilters() {
  const groups = ["Todas", ...new Set(state.products.map((product) => product.category.split(".")[0]))];
  elements.categoryFilters.innerHTML = groups.map((group) => {
    const label = group === "Todas" ? group : categoryNames[group] ?? group;
    return `<button class="filter-button" type="button" data-category="${escapeHtml(group)}" aria-pressed="${state.category === group}">${escapeHtml(label)}</button>`;
  }).join("");
}

function renderProducts() {
  const products = filteredProducts();
  elements.productTotal.textContent = `${products.length} itens`;
  elements.productGrid.innerHTML = products.length ? products.map((product, index) => `
    <article class="product-card">
      <div class="product-art">
        <span class="stock-label ${product.stock === 0 ? "out" : ""}">${product.stock > 0 ? `${product.stock} em estoque` : "Indisponível"}</span>
        <span class="art-id">${String(product.id).padStart(2, "0")}</span>
        <span class="art-category">${escapeHtml(categoryLabel(product.category))}</span>
      </div>
      <div class="product-info">
        <p class="product-name">${escapeHtml(product.name)}</p>
        <div class="product-meta">
          <span class="product-price">${currency.format(product.price)}</span>
          <button class="add-button" type="button" data-add="${product.id}" aria-label="Adicionar ${escapeHtml(product.name)} ao carrinho" title="Adicionar ao carrinho" ${product.stock === 0 ? "disabled" : ""}>
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 5v14M5 12h14" /></svg>
          </button>
        </div>
      </div>
    </article>`).join("") : '<p class="no-products">Nenhum produto encontrado com esses filtros.</p>';
}

function renderCart() {
  const entries = [...state.cart.entries()];
  const itemCount = entries.reduce((total, [, quantity]) => total + quantity, 0);
  const subtotal = entries.reduce((total, [id, quantity]) => {
    const product = state.products.find((candidate) => candidate.id === id);
    return total + (product?.price ?? 0) * quantity;
  }, 0);

  elements.cartCount.textContent = itemCount;
  elements.cartHeadingCount.textContent = `(${itemCount})`;
  elements.cartSubtotal.textContent = currency.format(subtotal);
  elements.submitOrder.disabled = itemCount === 0;
  elements.cartItems.innerHTML = entries.length ? entries.map(([id, quantity]) => {
    const product = state.products.find((candidate) => candidate.id === id);
    if (!product) return "";
    return `<div class="cart-line">
      <div><p class="cart-line-name">${escapeHtml(product.name)}</p><span class="cart-line-price">${currency.format(product.price)} cada</span></div>
      <div class="quantity-controls">
        <button class="quantity-button" type="button" data-quantity="${id}" data-change="-1" aria-label="Diminuir quantidade">−</button>
        <span class="quantity-value">${quantity}</span>
        <button class="quantity-button" type="button" data-quantity="${id}" data-change="1" aria-label="Aumentar quantidade" ${quantity >= product.stock ? "disabled" : ""}>+</button>
        <button class="remove-item" type="button" data-remove="${id}" aria-label="Remover ${escapeHtml(product.name)}">
          <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7h16M10 11v6m4-6v6M5 7l1 14h12l1-14M9 7V4h6v3" /></svg>
        </button>
      </div>
    </div>`;
  }).join("") : '<p class="empty-state">Seu carrinho está vazio.<br />Adicione uma peça para começar.</p>';
}

// Label do evento SSE → o que a linha do pedido deve passar a mostrar.
// O servidor manda apenas o label e o id; o texto do status é daqui.
const ORDER_EVENTS = {
  "estoque-confirmado": { status: "Estoque confirmado" },
  "aguardando-pagamento": { status: "Aguardando pagamento" },
  "pagamento-aprovado": { status: "Pagamento aprovado" },
  "pedido-enviado": { status: "Enviado" },
  // Falta de estoque não chega a reservar nada, então o catálogo não muda.
  "estoque-indisponivel": { removed: true },
  // A exclusão por recusa devolve a reserva: o evento traz os itens repostos.
  "pagamento-recusado": { removed: true, restoresStock: true },
};

function statusClassFor(status) {
  return /recusado/i.test(status) ? "rejected" : /indisponível/i.test(status) ? "unavailable" : /enviado/i.test(status) ? "shipped" : /aguardando/i.test(status) ? "pending" : "";
}

function paymentLinkHtml(paymentUrl, status) {
  return paymentUrl && /aguardando/i.test(status)
    ? `<a class="payment-link" href="${escapeHtml(paymentUrl)}" target="_blank" rel="noopener">Pagar agora</a>`
    : "";
}

function renderOrders(orders) {
  elements.ordersList.innerHTML = orders.length ? orders.map((order) => {
    const status = order.status ?? "Criado";
    const itemSummary = order.items.map((item) => `${item.quantity}× ${escapeHtml(item.name)}`).join(" · ");
    const date = new Date(order.createdAt).toLocaleString("pt-BR", { dateStyle: "short", timeStyle: "short" });
    return `<article class="order-row" data-order-id="${escapeHtml(order.orderId)}">
      <div class="order-topline"><span class="order-number">Pedido #${order.orderNumber}</span><span class="status-pill ${statusClassFor(status)}">${escapeHtml(status)}</span></div>
      <p class="order-details">${itemSummary}<br />${date}</p>
      ${paymentLinkHtml(order.paymentUrl, status)}
    </article>`;
  }).join("") : '<p class="empty-state">Ainda não há pedidos para este cliente.</p>';
}

function findOrderRow(orderId) {
  return elements.ordersList.querySelector(`[data-order-id="${orderId}"]`);
}

/// Atualiza somente a linha daquele pedido: selo de status e link de pagamento.
/// Ignora pedidos que não estão na tela — nada é buscado na API.
function patchOrderRow(orderId, status, paymentUrl) {
  const row = findOrderRow(orderId);
  if (!row) return;

  const pill = row.querySelector(".status-pill");
  pill.textContent = status;
  pill.className = `status-pill ${statusClassFor(status)}`;

  const link = row.querySelector(".payment-link");
  const linkHtml = paymentLinkHtml(paymentUrl, status);

  if (linkHtml && link) link.href = paymentUrl;
  else if (linkHtml) row.insertAdjacentHTML("beforeend", linkHtml);
  else link?.remove();
}

/// Repõe em memória o estoque devolvido pela exclusão do pedido.
function restoreStock(releasedItems) {
  for (const item of releasedItems ?? []) {
    const product = state.products.find((candidate) => candidate.id === item.productId);
    if (product) product.stock += item.quantity;
  }

  renderProducts();
  renderCart();
}

/// Pedido cancelado pelo estoque ou pelo pagamento: sai da lista.
function removeOrderRow(orderId) {
  findOrderRow(orderId)?.remove();

  if (!elements.ordersList.querySelector("[data-order-id]")) {
    elements.ordersList.innerHTML = '<p class="empty-state">Ainda não há pedidos para este cliente.</p>';
  }
}

async function loadProducts() {
  elements.catalogMessage.textContent = "";
  try {
    const response = await fetch(`${API_BASE}/api/products`);
    if (!response.ok) throw new Error("Não foi possível consultar o estoque.");
    state.products = await response.json();
    renderPromotionCategories();
    renderFilters();
    renderProducts();
    renderCart();
  } catch (error) {
    elements.catalogMessage.textContent = `${error.message} Verifique se os serviços de estoque e pedidos estão iniciados.`;
    elements.productGrid.innerHTML = "";
    elements.productTotal.textContent = "indisponível";
  }
}

async function loadOrders() {
  const customerId = elements.customerId.value.trim();
  if (!customerId) {
    elements.ordersList.innerHTML = '<p class="empty-state">Informe seu identificador de cliente.</p>';
    return;
  }

  try {
    const response = await fetch(`${API_BASE}/api/orders?customerId=${encodeURIComponent(customerId)}`);
    if (!response.ok) throw new Error("Falha ao consultar pedidos.");
    renderOrders(await response.json());
  } catch {
    elements.ordersList.innerHTML = '<p class="empty-state">Não foi possível carregar seus pedidos.</p>';
  }
}

elements.customerId.value = localStorage.getItem("customerId") || elements.customerId.value;
elements.customerId.addEventListener("change", () => {
  localStorage.setItem("customerId", elements.customerId.value.trim());
  loadOrders();
  connectStream(elements.customerId.value.trim());
});

elements.categoryFilters.addEventListener("click", (event) => {
  const button = event.target.closest("[data-category]");
  if (!button) return;
  state.category = button.dataset.category;
  renderFilters();
  renderProducts();
});

elements.search.addEventListener("input", () => {
  state.search = elements.search.value;
  renderProducts();
});

elements.availableOnly.addEventListener("change", () => {
  state.availableOnly = elements.availableOnly.checked;
  renderProducts();
});

elements.productGrid.addEventListener("click", (event) => {
  const button = event.target.closest("[data-add]");
  if (!button) return;
  const product = state.products.find((candidate) => candidate.id === Number(button.dataset.add));
  if (!product) return;
  const quantity = state.cart.get(product.id) ?? 0;
  if (quantity < product.stock) state.cart.set(product.id, quantity + 1);
  renderCart();
});

elements.cartItems.addEventListener("click", (event) => {
  const removeButton = event.target.closest("[data-remove]");
  if (removeButton) {
    state.cart.delete(Number(removeButton.dataset.remove));
    renderCart();
    return;
  }

  const quantityButton = event.target.closest("[data-quantity]");
  if (!quantityButton) return;
  const id = Number(quantityButton.dataset.quantity);
  const nextQuantity = (state.cart.get(id) ?? 0) + Number(quantityButton.dataset.change);
  const product = state.products.find((candidate) => candidate.id === id);
  if (nextQuantity <= 0) state.cart.delete(id);
  else if (product && nextQuantity <= product.stock) state.cart.set(id, nextQuantity);
  renderCart();
});

elements.submitOrder.addEventListener("click", async () => {
  const customerId = elements.customerId.value.trim();
  if (!customerId) {
    elements.orderFeedback.textContent = "Informe seu identificador de cliente antes de finalizar.";
    elements.orderFeedback.classList.add("error");
    elements.customerId.focus();
    return;
  }

  // Abre a aba agora, ainda dentro do clique, senão o bloqueador de pop-up
  // barra a abertura quando o link chegar pelo SSE segundos depois.
  state.paymentTab = window.open("about:blank", "_blank");

  elements.submitOrder.disabled = true;
  elements.orderFeedback.classList.remove("error");
  elements.orderFeedback.textContent = "Enviando pedido...";
  try {
    const response = await fetch(`${API_BASE}/api/orders`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        customerId,
        items: [...state.cart.entries()].map(([productId, quantity]) => ({
          productId,
          quantity,
          name: state.products.find((candidate) => candidate.id === productId)?.name ?? "",
        })),
      }),
    });
    const result = await response.json();
    if (!response.ok) throw new Error(result.detail ?? result.message ?? "Não foi possível criar o pedido.");
    state.cart.clear();
    renderCart();
    state.awaitingPaymentFor = result.orderId;
    elements.orderFeedback.textContent = `Pedido #${result.orderNumber} criado. Aguardando o link de pagamento...`;
    await Promise.all([loadOrders(), loadProducts()]);
  } catch (error) {
    closePaymentTab();
    state.awaitingPaymentFor = null;
    elements.orderFeedback.textContent = error.message;
    elements.orderFeedback.classList.add("error");
  } finally {
    elements.submitOrder.disabled = state.cart.size === 0;
  }
});

function closePaymentTab() {
  try {
    state.paymentTab?.close();
  } catch {
    // A aba pode já ter sido fechada pelo usuário.
  }
  state.paymentTab = null;
}

/// Abre o checkout: reaproveita a aba do clique ou cai no botão "Pagar agora".
function openCheckout(paymentUrl) {
  if (state.paymentTab && !state.paymentTab.closed) {
    state.paymentTab.location.href = paymentUrl;
    state.paymentTab.focus?.();
    elements.orderFeedback.textContent = "Conclua o pagamento na aba que acabou de abrir.";
  } else {
    elements.orderFeedback.textContent = 'Pagamento pronto: use o botão "Pagar agora" na lista de pedidos.';
  }

  state.paymentTab = null;
  state.awaitingPaymentFor = null;
}

/// Mantém a conexão SSE com o gateway para receber as mudanças de status.
function connectStream(customerId) {
  state.stream?.close();
  state.stream = null;

  if (!customerId) return;

  const stream = new EventSource(`${API_BASE}/api/orders/stream?customerId=${encodeURIComponent(customerId)}`);
  state.stream = stream;

  stream.onopen = () => console.log(`[SSE] conectado (cliente ${customerId})`);

  // Um listener por label. Eventos nomeados não caem no onmessage.
  for (const [label, rule] of Object.entries(ORDER_EVENTS)) {
    stream.addEventListener(label, (event) => {
      const { orderId, paymentUrl, releasedItems } = JSON.parse(event.data);
      console.log(`[SSE] ${label}`, { orderId, paymentUrl, releasedItems });

      if (rule.removed) {
        elements.orderFeedback.textContent = rule.restoresStock
          ? "Pagamento recusado. O pedido foi cancelado e os produtos retornam ao estoque."
          : "Sem estoque para os itens selecionados. O pedido foi cancelado.";
        elements.orderFeedback.classList.add("error");
        if (orderId === state.awaitingPaymentFor) {
          closePaymentTab();
          state.awaitingPaymentFor = null;
        }
        removeOrderRow(orderId);
        if (rule.restoresStock) restoreStock(releasedItems);
        return;
      }

      if (orderId === state.awaitingPaymentFor && paymentUrl) {
        openCheckout(paymentUrl);
      }

      // Só a linha daquele pedido é tocada; nenhuma requisição é feita.
      patchOrderRow(orderId, rule.status, paymentUrl);
    });
  }

  stream.onerror = () => {
    console.warn(`[SSE] erro, readyState = ${stream.readyState}`, "(0 conectando, 1 aberto, 2 fechado)");

    // O EventSource reconecta sozinho; só avisa se a conexão não voltar.
    if (stream.readyState === EventSource.CLOSED) {
      elements.orderFeedback.textContent = "Acompanhamento em tempo real indisponível. Use o botão Atualizar.";
    }
  };
}

document.querySelector("#refresh-orders").addEventListener("click", loadOrders);

loadProducts();
loadOrders();
connectStream(elements.customerId.value.trim());