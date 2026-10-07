const state = {
  products: [],
  cart: new Map(),
  category: "Todas",
  search: "",
  availableOnly: false,
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

function renderOrders(orders) {
  elements.ordersList.innerHTML = orders.length ? orders.map((order) => {
    const status = order.status ?? "Criado";
    const statusClass = /recusado/i.test(status) ? "rejected" : /indisponível/i.test(status) ? "unavailable" : /enviado/i.test(status) ? "shipped" : "";
    const itemSummary = order.items.map((item) => `${item.quantity}× ${escapeHtml(item.name)}`).join(" · ");
    const date = new Date(order.createdAt).toLocaleString("pt-BR", { dateStyle: "short", timeStyle: "short" });
    return `<article class="order-row">
      <div class="order-topline"><span class="order-number">Pedido #${order.orderNumber}</span><span class="status-pill ${statusClass}">${escapeHtml(status)}</span></div>
      <p class="order-details">${itemSummary}<br />${date}</p>
    </article>`;
  }).join("") : '<p class="empty-state">Ainda não há pedidos para este cliente.</p>';
}

async function loadProducts() {
  elements.catalogMessage.textContent = "";
  try {
    const response = await fetch("/api/products");
    if (!response.ok) throw new Error("Não foi possível consultar o estoque.");
    state.products = await response.json();
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
    const response = await fetch(`/api/orders?customerId=${encodeURIComponent(customerId)}`);
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

  elements.submitOrder.disabled = true;
  elements.orderFeedback.classList.remove("error");
  elements.orderFeedback.textContent = "Enviando pedido...";
  try {
    const response = await fetch("/api/orders", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        customerId,
        items: [...state.cart.entries()].map(([productId, quantity]) => ({ productId, quantity })),
      }),
    });
    const result = await response.json();
    if (!response.ok) throw new Error(result.detail ?? result.message ?? "Não foi possível criar o pedido.");
    state.cart.clear();
    renderCart();
    elements.orderFeedback.textContent = `Pedido #${result.orderNumber} criado e enviado para processamento.`;
    await Promise.all([loadOrders(), loadProducts()]);
  } catch (error) {
    elements.orderFeedback.textContent = error.message;
    elements.orderFeedback.classList.add("error");
  } finally {
    elements.submitOrder.disabled = state.cart.size === 0;
  }
});

document.querySelector("#refresh-orders").addEventListener("click", loadOrders);

loadProducts();
loadOrders();
window.setInterval(loadOrders, 5000);