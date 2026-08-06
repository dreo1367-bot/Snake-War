import pygame
import random
import sys

# Initialize Pygame
pygame.init()

# Constants
SCREEN_WIDTH = 800
SCREEN_HEIGHT = 600
GRID_SIZE = 20
GRID_WIDTH = SCREEN_WIDTH // GRID_SIZE
GRID_HEIGHT = SCREEN_HEIGHT // GRID_SIZE

# Colors
BLACK = (0, 0, 0)
WHITE = (255, 255, 255)
GREEN = (0, 255, 0)
DARK_GREEN = (0, 200, 0)
RED = (255, 0, 0)
GRAY = (50, 50, 50)
LIGHT_GRAY = (100, 100, 100)

# Directions
UP = (0, -1)
DOWN = (0, 1)
LEFT = (-1, 0)
RIGHT = (1, 0)


class GameOfLife:
    """Conway's Game of Life for background animation"""
    
    def __init__(self, width, height):
        self.width = width
        self.height = height
        self.grid = [[random.choice([0, 1]) for _ in range(width)] for _ in range(height)]
        
    def update(self):
        """Update the Game of Life grid"""
        new_grid = [[0 for _ in range(self.width)] for _ in range(self.height)]
        
        for y in range(self.height):
            for x in range(self.width):
                neighbors = self.count_neighbors(x, y)
                if self.grid[y][x] == 1:
                    if neighbors < 2 or neighbors > 3:
                        new_grid[y][x] = 0
                    else:
                        new_grid[y][x] = 1
                else:
                    if neighbors == 3:
                        new_grid[y][x] = 1
                        
        self.grid = new_grid
        
    def count_neighbors(self, x, y):
        """Count live neighbors for a cell"""
        count = 0
        for dy in [-1, 0, 1]:
            for dx in [-1, 0, 1]:
                if dx == 0 and dy == 0:
                    continue
                nx, ny = (x + dx) % self.width, (y + dy) % self.height
                count += self.grid[ny][nx]
        return count
    
    def draw(self, surface, alpha=50):
        """Draw the Game of Life grid with transparency effect"""
        for y in range(self.height):
            for x in range(self.width):
                if self.grid[y][x] == 1:
                    color = (alpha, alpha, alpha)
                    rect = pygame.Rect(x * GRID_SIZE, y * GRID_SIZE, GRID_SIZE - 1, GRID_SIZE - 1)
                    pygame.draw.rect(surface, color, rect)


class Snake:
    """Snake game logic"""
    
    def __init__(self):
        self.reset()
        
    def reset(self):
        """Reset snake to initial state"""
        self.body = [(GRID_WIDTH // 2, GRID_HEIGHT // 2)]
        self.direction = RIGHT
        self.grow = False
        
    def move(self):
        """Move the snake"""
        head_x, head_y = self.body[0]
        dir_x, dir_y = self.direction
        new_head = (head_x + dir_x, head_y + dir_y)
        
        # Wrap around screen
        new_head = (new_head[0] % GRID_WIDTH, new_head[1] % GRID_HEIGHT)
        
        self.body.insert(0, new_head)
        
        if not self.grow:
            self.body.pop()
        else:
            self.grow = False
            
    def change_direction(self, new_direction):
        """Change direction (prevent 180-degree turns)"""
        if (new_direction[0] * -1, new_direction[1] * -1) != self.direction:
            self.direction = new_direction
            
    def check_collision(self):
        """Check if snake collided with itself"""
        head = self.body[0]
        return head in self.body[1:]
    
    def draw(self, surface):
        """Draw the snake"""
        for i, segment in enumerate(self.body):
            x, y = segment
            rect = pygame.Rect(x * GRID_SIZE, y * GRID_SIZE, GRID_SIZE - 2, GRID_SIZE - 2)
            color = GREEN if i == 0 else DARK_GREEN
            pygame.draw.rect(surface, color, rect)
            pygame.draw.rect(surface, WHITE, rect, 1)


class Food:
    """Food for the snake"""
    
    def __init__(self):
        self.position = (0, 0)
        self.spawn()
        
    def spawn(self, snake_body=None):
        """Spawn food at random position (not on snake)"""
        while True:
            self.position = (random.randint(0, GRID_WIDTH - 1), 
                           random.randint(0, GRID_HEIGHT - 1))
            if snake_body is None or self.position not in snake_body:
                break
                
    def draw(self, surface):
        """Draw the food"""
        x, y = self.position
        rect = pygame.Rect(x * GRID_SIZE, y * GRID_SIZE, GRID_SIZE - 2, GRID_SIZE - 2)
        pygame.draw.rect(surface, RED, rect)
        pygame.draw.rect(surface, WHITE, rect, 1)


class Button:
    """Menu button"""
    
    def __init__(self, x, y, width, height, text, action=None):
        self.rect = pygame.Rect(x, y, width, height)
        self.text = text
        self.action = action
        self.hovered = False
        
    def draw(self, surface, font):
        """Draw the button"""
        color = LIGHT_GRAY if self.hovered else GRAY
        pygame.draw.rect(surface, color, self.rect)
        pygame.draw.rect(surface, WHITE, self.rect, 2)
        
        text_surface = font.render(self.text, True, WHITE)
        text_rect = text_surface.get_rect(center=self.rect.center)
        surface.blit(text_surface, text_rect)
        
    def is_hovered(self, pos):
        """Check if mouse is hovering over button"""
        self.hovered = self.rect.collidepoint(pos)
        return self.hovered


class Game:
    """Main game class"""
    
    def __init__(self):
        self.screen = pygame.display.set_mode((SCREEN_WIDTH, SCREEN_HEIGHT))
        pygame.display.set_caption("Snake Game")
        self.clock = pygame.time.Clock()
        self.font = pygame.font.Font(None, 48)
        self.small_font = pygame.font.Font(None, 36)
        
        self.snake = Snake()
        self.food = Food()
        self.game_of_life = GameOfLife(GRID_WIDTH, GRID_HEIGHT)
        
        self.score = 0
        self.high_score = 0
        self.game_over = False
        self.paused = False
        self.in_menu = True
        
        # Create buttons
        button_width = 200
        button_height = 50
        start_x = (SCREEN_WIDTH - button_width) // 2
        
        self.buttons = [
            Button(start_x, 250, button_width, button_height, "Start Game", self.start_game),
            Button(start_x, 320, button_width, button_height, "Quit", self.quit_game)
        ]
        
        self.game_speed = 10
        
    def start_game(self):
        """Start a new game"""
        self.snake.reset()
        self.food.spawn(self.snake.body)
        self.score = 0
        self.game_over = False
        self.paused = False
        self.in_menu = False
        
    def quit_game(self):
        """Quit the game"""
        pygame.quit()
        sys.exit()
        
    def handle_events(self):
        """Handle pygame events"""
        for event in pygame.event.get():
            if event.type == pygame.QUIT:
                self.quit_game()
                
            elif event.type == pygame.KEYDOWN:
                if self.in_menu:
                    if event.key == pygame.K_RETURN:
                        self.start_game()
                    elif event.key == pygame.K_ESCAPE:
                        self.quit_game()
                        
                elif self.game_over:
                    if event.key == pygame.K_RETURN:
                        self.start_game()
                    elif event.key == pygame.K_ESCAPE:
                        self.in_menu = True
                        
                else:
                    if event.key == pygame.K_p:
                        self.paused = not self.paused
                    elif event.key == pygame.K_ESCAPE:
                        self.in_menu = True
                    elif event.key == pygame.K_UP or event.key == pygame.K_w:
                        self.snake.change_direction(UP)
                    elif event.key == pygame.K_DOWN or event.key == pygame.K_s:
                        self.snake.change_direction(DOWN)
                    elif event.key == pygame.K_LEFT or event.key == pygame.K_a:
                        self.snake.change_direction(LEFT)
                    elif event.key == pygame.K_RIGHT or event.key == pygame.K_d:
                        self.snake.change_direction(RIGHT)
                        
            elif event.type == pygame.MOUSEMOTION:
                for button in self.buttons:
                    button.is_hovered(event.pos)
                    
            elif event.type == pygame.MOUSEBUTTONDOWN:
                if event.button == 1:  # Left click
                    for button in self.buttons:
                        if button.is_hovered(event.pos):
                            if button.action:
                                button.action()
                                
    def update(self):
        """Update game state"""
        if self.in_menu:
            self.game_of_life.update()
            return
            
        if self.game_over or self.paused:
            return
            
        self.snake.move()
        
        # Check collision with self
        if self.snake.check_collision():
            self.game_over = True
            if self.score > self.high_score:
                self.high_score = self.score
                
        # Check if food eaten
        if self.snake.body[0] == self.food.position:
            self.snake.grow = True
            self.score += 10
            self.food.spawn(self.snake.body)
            
            # Increase speed slightly
            if self.game_speed < 20:
                self.game_speed += 0.2
                
    def draw_menu(self):
        """Draw the main menu"""
        self.game_of_life.draw(self.screen, alpha=60)
        
        # Draw title
        title = self.font.render("SNAKE", True, GREEN)
        title_rect = title.get_rect(center=(SCREEN_WIDTH // 2, 150))
        self.screen.blit(title, title_rect)
        
        # Draw subtitle
        subtitle = self.small_font.render("Classic Snake Game", True, WHITE)
        subtitle_rect = subtitle.get_rect(center=(SCREEN_WIDTH // 2, 200))
        self.screen.blit(subtitle, subtitle_rect)
        
        # Draw buttons
        for button in self.buttons:
            button.draw(self.screen, self.small_font)
            
        # Draw high score
        if self.high_score > 0:
            hs_text = self.small_font.render(f"High Score: {self.high_score}", True, WHITE)
            hs_rect = hs_text.get_rect(center=(SCREEN_WIDTH // 2, 450))
            self.screen.blit(hs_text, hs_rect)
            
        # Draw instructions
        inst_text = self.small_font.render("Press ENTER to Start", True, LIGHT_GRAY)
        inst_rect = inst_text.get_rect(center=(SCREEN_WIDTH // 2, 520))
        self.screen.blit(inst_text, inst_rect)
        
    def draw_game(self):
        """Draw the game"""
        # Draw Game of Life background (dimmed)
        self.game_of_life.draw(self.screen, alpha=30)
        
        # Draw grid lines (subtle)
        for x in range(0, SCREEN_WIDTH, GRID_SIZE):
            pygame.draw.line(self.screen, (30, 30, 30), (x, 0), (x, SCREEN_HEIGHT))
        for y in range(0, SCREEN_HEIGHT, GRID_SIZE):
            pygame.draw.line(self.screen, (30, 30, 30), (0, y), (SCREEN_WIDTH, y))
        
        self.food.draw(self.screen)
        self.snake.draw(self.screen)
        
        # Draw score
        score_text = self.small_font.render(f"Score: {self.score}", True, WHITE)
        self.screen.blit(score_text, (10, 10))
        
        # Draw pause indicator
        if self.paused:
            pause_text = self.font.render("PAUSED", True, WHITE)
            pause_rect = pause_text.get_rect(center=(SCREEN_WIDTH // 2, SCREEN_HEIGHT // 2))
            pygame.draw.rect(self.screen, BLACK, pause_rect.inflate(40, 20))
            self.screen.blit(pause_text, pause_rect)
            
        # Draw game over
        if self.game_over:
            go_text = self.font.render("GAME OVER", True, RED)
            go_rect = go_text.get_rect(center=(SCREEN_WIDTH // 2, SCREEN_HEIGHT // 2 - 30))
            pygame.draw.rect(self.screen, BLACK, go_rect.inflate(40, 20))
            self.screen.blit(go_text, go_rect)
            
            restart_text = self.small_font.render("Press ENTER to Restart", True, WHITE)
            restart_rect = restart_text.get_rect(center=(SCREEN_WIDTH // 2, SCREEN_HEIGHT // 2 + 20))
            self.screen.blit(restart_text, restart_rect)
            
            menu_text = self.small_font.render("Press ESC for Menu", True, LIGHT_GRAY)
            menu_rect = menu_text.get_rect(center=(SCREEN_WIDTH // 2, SCREEN_HEIGHT // 2 + 50))
            self.screen.blit(menu_text, menu_rect)
            
    def draw(self):
        """Draw everything"""
        self.screen.fill(BLACK)
        
        if self.in_menu:
            self.draw_menu()
        else:
            self.draw_game()
            
        pygame.display.flip()
        
    def run(self):
        """Main game loop"""
        running = True
        while running:
            self.handle_events()
            self.update()
            self.draw()
            self.clock.tick(60)  # 60 FPS for smooth animations
            
            # Control game speed
            if not self.in_menu and not self.game_over and not self.paused:
                pygame.time.delay(int(1000 / self.game_speed))


def main():
    """Entry point"""
    game = Game()
    game.run()


if __name__ == "__main__":
    main()
