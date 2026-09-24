import { Injectable } from '@angular/core';
import { CartLine, Category, CheckoutCustomer, CheckoutResult, CustomerOrder, Product, Store } from '../models/storefront.models';
import { StorefrontDataProvider } from './storefront-data.provider';

const guturgoCategories: Category[] = [
  { id: 'all', name: 'All', emoji: '✨' },
  { id: 'dairy', name: 'Dairy & Eggs', emoji: '🥛' },
  { id: 'fruits', name: 'Fruits & Veg', emoji: '🥑' },
  { id: 'bakery', name: 'Bakery', emoji: '🥐' },
  { id: 'pantry', name: 'Pantry', emoji: '🫙' },
  { id: 'snacks', name: 'Snacks', emoji: '🍪' },
];

const guturgoProducts: Product[] = [
  { id: 'amul-gold-milk', categoryId: 'dairy', name: 'Amul Gold Milk', description: 'Rich and creamy full-cream milk, perfect for your morning chai.', imageUrl: 'https://images.unsplash.com/photo-1563636619-e9143da7973b?auto=format&fit=crop&w=720&q=85', sellingPrice: 68, available: true, unitLabel: '1 L', badge: 'Bestseller' },
  { id: 'amul-butter', categoryId: 'dairy', name: 'Amul Butter', description: 'A little butter makes everything better. Salted and delicious.', imageUrl: 'https://images.unsplash.com/photo-1589985270826-4b7bb135bc9d?auto=format&fit=crop&w=720&q=85', sellingPrice: 285, compareAtPrice: 300, available: true, unitLabel: '500 g' },
  { id: 'farm-eggs', categoryId: 'dairy', name: 'Farm Fresh Eggs', description: 'A fresh, protein-rich dozen from our trusted local farm.', imageUrl: 'https://images.unsplash.com/photo-1506976785307-8732e854ad03?auto=format&fit=crop&w=720&q=85', sellingPrice: 96, available: true, unitLabel: '12 pcs', badge: 'Fresh today' },
  { id: 'red-apples', categoryId: 'fruits', name: 'Kashmiri Red Apples', description: 'Crisp, sweet apples selected for freshness and flavour.', imageUrl: 'https://images.unsplash.com/photo-1560806887-1e4cd0b6cbd6?auto=format&fit=crop&w=720&q=85', sellingPrice: 180, available: true, unitLabel: '1 kg' },
  { id: 'baby-spinach', categoryId: 'fruits', name: 'Tender Baby Spinach', description: 'Fresh greens, washed and ready for your favourite recipes.', imageUrl: 'https://images.unsplash.com/photo-1576045057995-568f588f82fb?auto=format&fit=crop&w=720&q=85', sellingPrice: 35, available: true, unitLabel: '1 bunch' },
  { id: 'sourdough-bread', categoryId: 'bakery', name: 'Country Sourdough Bread', description: 'Small-batch baked with a golden crust and a soft centre.', imageUrl: 'https://images.unsplash.com/photo-1585478259715-876acc5be8eb?auto=format&fit=crop&w=720&q=85', sellingPrice: 120, available: true, unitLabel: '400 g', badge: 'Baked today' },
  { id: 'basmati-rice', categoryId: 'pantry', name: 'India Gate Basmati Rice', description: 'Long, fragrant grains for everyday meals and special occasions.', imageUrl: 'https://images.unsplash.com/photo-1586201375761-83865001e31c?auto=format&fit=crop&w=720&q=85', sellingPrice: 165, available: true, unitLabel: '1 kg' },
  { id: 'masala-chai', categoryId: 'pantry', name: 'Taj Mahal Tea', description: 'A robust, full-bodied tea blend for your daily cup.', imageUrl: 'https://images.unsplash.com/photo-1576092768241-dec231879fc3?auto=format&fit=crop&w=720&q=85', sellingPrice: 145, available: true, unitLabel: '250 g' },
  { id: 'good-day-cookies', categoryId: 'snacks', name: 'Britannia Good Day Cookies', description: 'Buttery, crunchy cookies made for sharing over chai.', imageUrl: 'https://images.unsplash.com/photo-1558961363-fa8fdf82db35?auto=format&fit=crop&w=720&q=85', sellingPrice: 35, available: true, unitLabel: '200 g' },
  { id: 'mango-yoghurt', categoryId: 'dairy', name: 'Mango Greek Yoghurt', description: 'Thick, creamy yoghurt with a bright mango swirl.', imageUrl: 'https://images.unsplash.com/photo-1488477181946-6428a0291777?auto=format&fit=crop&w=720&q=85', sellingPrice: 55, available: false, unitLabel: '100 g' },
];

const demoStore: Store = {
  storeKey: 'guturgo',
  name: 'GuturGo',
  tagline: 'Good things, close to home.',
  accentColor: '#145c43',
  deliveryMessage: 'Free delivery on orders above ₹499',
};

/** GuturGo demo data, selected only by the explicit mock build configuration. */
@Injectable({ providedIn: 'root' })
export class DevelopmentStorefrontDataProvider implements StorefrontDataProvider {
  async getStore(storeKey: string): Promise<Store | null> {
    return storeKey.toLowerCase() === demoStore.storeKey ? demoStore : null;
  }

  async getCategories(storeKey: string): Promise<Category[]> {
    return storeKey.toLowerCase() === demoStore.storeKey ? guturgoCategories : [];
  }

  async getProducts(storeKey: string): Promise<Product[]> {
    return storeKey.toLowerCase() === demoStore.storeKey ? guturgoProducts : [];
  }

  async getProduct(storeKey: string, productId: string): Promise<Product | null> {
    if (storeKey.toLowerCase() !== demoStore.storeKey) return null;
    return guturgoProducts.find((product) => product.id === productId) ?? null;
  }

  async getOrders(_storeKey: string): Promise<CustomerOrder[]> {
    return [];
  }

  async checkoutWithRazorpay(_storeKey: string, _customer: CheckoutCustomer, lines: readonly CartLine[], _idempotencyKey: string): Promise<CheckoutResult> {
    return { orderId: crypto.randomUUID(), orderNumber: 'MOCK-ORDER', amount: lines.reduce((sum, line) => sum + line.product.sellingPrice * line.quantity, 0), currency: 'INR', paymentId: crypto.randomUUID(), checkoutUrl: '' };
  }
}
