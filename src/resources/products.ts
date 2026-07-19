import type { HttpClientConfig } from "../http.js";
import { request } from "../http.js";
import type { Environment, Product, ProductCreateRequest, ProductListParams } from "../types.js";

export class Products {
  constructor(private readonly config: HttpClientConfig) {}

  async create(params: ProductCreateRequest): Promise<Product> {
    const res = await request<{ ok: true; environment: Environment; product: Product }>(
      this.config, "POST", "products", params,
    );
    return res.product;
  }

  async list(params?: ProductListParams): Promise<Product[]> {
    const qs = params?.cycle ? `?cycle=${encodeURIComponent(params.cycle)}` : "";
    const res = await request<{ ok: true; environment: Environment; products: Product[] }>(
      this.config, "GET", `products${qs}`,
    );
    return res.products;
  }

  async retrieve(id: string): Promise<Product> {
    const res = await request<{ ok: true; environment: Environment; product: Product }>(
      this.config, "GET", `products/${id}`,
    );
    return res.product;
  }

  async delete(id: string): Promise<void> {
    await request(this.config, "DELETE", `products/${id}`);
  }
}
